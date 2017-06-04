using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AssemblyBuilderPOC
{
    public class DynamicAssemblyBuilder
    {
        public const string CommandSuffix = "Command";

        private static readonly Dictionary<Assembly, ModuleBuilder> Builders;

        static DynamicAssemblyBuilder()
        {
            Builders = new Dictionary<Assembly, ModuleBuilder>();
        }

        private static ModuleBuilder GetModuleBuilder(Assembly assembly)
        {
            if (!Builders.ContainsKey(assembly))
                Builders.Add(assembly, CreateNewModuleBuilder());

            return Builders[assembly];
        }

        private static ModuleBuilder CreateNewModuleBuilder()
        {
            var assemblyName = new AssemblyName($"DynamicTypes.{Guid.NewGuid():N}");
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            return assembly.DefineDynamicModule(assemblyName.Name);
        }

        public static object CreateViewModelInstance(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var typeBuilder = CreateNewTypeBuilder(type);
            var methods = type.GetMethods();
            foreach (var method in methods)
            {
                var containsMyCommandAttribute = method.GetCustomAttribute<CommandAttribute>() != null;
                if (!containsMyCommandAttribute)
                    continue;

                var propertyName = $"{method.Name}{CommandSuffix}";
                var field = typeBuilder.DefineField($"_{propertyName}", typeof(ICommand), FieldAttributes.Private);

                var getMethod = typeBuilder.DefineMethod($"get_{propertyName}", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, typeof(ICommand), null);
                var ilGenerator = getMethod.GetILGenerator();
                CreateGetterBody(ilGenerator, field, method);

                var commandProperty = typeBuilder.DefineProperty(propertyName, PropertyAttributes.None, typeof(ICommand), null);
                commandProperty.SetGetMethod(getMethod);
            }

            var newViewModelType = typeBuilder.CreateType();
            return Activator.CreateInstance(newViewModelType);
        }

        private static TypeBuilder CreateNewTypeBuilder(Type type)
        {
            var moduleBuilder = GetModuleBuilder(type.Assembly);
            var types = new List<Type>
            {
                // typeof (INotifyPropertyChanged),
            };

            var name = $"{type.Name}_{Guid.NewGuid():N}";
            return moduleBuilder.DefineType(name, TypeAttributes.Public, type, types.ToArray());
        }

        private static void CreateGetterBody(ILGenerator ilGenerator, FieldBuilder commandField, MethodInfo methodToExecute)
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
            var actionConstructor = CreateActionConstructor();
            var delegateCommandConstructor = CreateDelegateCommandConstructor();
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

        private static ConstructorInfo CreateActionConstructor()
        {
            var inputParameters = new Type[]
            {
                typeof(object),
                typeof(IntPtr)
            };
            return typeof(Action).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, inputParameters, null);
        }

        private static ConstructorInfo CreateDelegateCommandConstructor()
        {
            var inputParameters = new Type[]
            {
                typeof(Action),
            };
            return typeof(DelegateCommand).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, inputParameters, null);
        }
    }
}
