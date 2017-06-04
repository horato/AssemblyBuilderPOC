using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Prism.Commands;

namespace AssemblyBuilderPOC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static ModuleBuilder _moduleBuilder;

        private static ModuleBuilder ModuleBuilder
        {
            get
            {
                if (_moduleBuilder == null)
                    _moduleBuilder = CreateModuleBuilder();

                return _moduleBuilder;
            }
        }

        private static ModuleBuilder CreateModuleBuilder()
        {
            var assemblyName = new AssemblyName($"DynamicTypes.{Guid.NewGuid():N}");
            return AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run).DefineDynamicModule(assemblyName.Name);
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = Activator.CreateInstance(CreateViewModel(typeof(MyViewModel)));
        }

        private Type CreateViewModel(Type type)
        {
            var typeBuilder = GetTypeBuilder(type);
            var methods = type.GetMethods();
            foreach (var method in methods)
            {
                var containsMyCommandAttribute = method.GetCustomAttribute<MyCommandAttribute>() != null;
                if (!containsMyCommandAttribute)
                    continue;

                var propertyName = method.Name + "Command";
                var field = typeBuilder.DefineField($"_{propertyName}", typeof(ICommand), FieldAttributes.Private);

                var getMethod = typeBuilder.DefineMethod($"get_{propertyName}", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, typeof(ICommand), null);
                var ilGenerator = getMethod.GetILGenerator();
                CreateGetterBody(ilGenerator, field, method);

                var commandProperty = typeBuilder.DefineProperty(propertyName, PropertyAttributes.None, typeof(ICommand), null);
                commandProperty.SetGetMethod(getMethod);
            }

            return typeBuilder.CreateType();
        }

        private void CreateGetterBody(ILGenerator ilGenerator, FieldBuilder commandField, MethodInfo methodToExecute)
        {
            ilGenerator.DeclareLocal(typeof(ICommand));
            ilGenerator.DeclareLocal(typeof(bool));

            var returnLabel = ilGenerator.DefineLabel();
            ilGenerator.Emit(OpCodes.Nop);

            //if(command field == null)
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, commandField);
            ilGenerator.Emit(OpCodes.Ldnull);
            ilGenerator.Emit(OpCodes.Ceq);
            ilGenerator.Emit(OpCodes.Ldc_I4_0);
            ilGenerator.Emit(OpCodes.Ceq);
            ilGenerator.Emit(OpCodes.Stloc_1);

            ilGenerator.Emit(OpCodes.Ldloc_1);
            ilGenerator.Emit(OpCodes.Brtrue_S, returnLabel);

            //command field = new DelegateCommand(new Action(method))
            var actionConstructor = GetActionConstructor();
            var delegateCommandConstructor = GetDelegateCommandConstructor();
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldftn, methodToExecute);
            ilGenerator.Emit(OpCodes.Newobj, actionConstructor);
            ilGenerator.Emit(OpCodes.Newobj, delegateCommandConstructor);
            ilGenerator.Emit(OpCodes.Stfld, commandField);

            var endLabel = ilGenerator.DefineLabel();
            //return command field
            ilGenerator.MarkLabel(returnLabel);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, commandField);
            ilGenerator.Emit(OpCodes.Stloc_0);
            ilGenerator.Emit(OpCodes.Br_S, endLabel);

            //function end
            ilGenerator.MarkLabel(endLabel);
            ilGenerator.Emit(OpCodes.Ldloc_0);
            ilGenerator.Emit(OpCodes.Ret);
        }

        private ConstructorInfo GetDelegateCommandConstructor()
        {
            var inputParameters = new Type[]
            {
                typeof(Action),
            };
            return typeof(DelegateCommand).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, inputParameters, null);
        }

        private ConstructorInfo GetActionConstructor()
        {
            var inputParameters = new Type[]
            {
                typeof(object),
                typeof(IntPtr)
            };
            return typeof(Action).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, inputParameters, null);
        }

        private TypeBuilder GetTypeBuilder(Type type)
        {
            var types = new List<Type>
            {
                // typeof (INotifyPropertyChanged),
            };

            var name = $"{type.Name}_{Guid.NewGuid():N}";
            return ModuleBuilder.DefineType(name, TypeAttributes.Public, type, types.ToArray());
        }

        public class MyViewModel
        {
            [MyCommand]
            public void MyAction()
            {
                MessageBox.Show("Hi");
            }
        }

        private class MyCommandAttribute : Attribute
        {
        }
    }
}
