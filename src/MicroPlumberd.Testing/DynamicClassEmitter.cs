using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;
using SpecFlow.Internal.Json;
using TechTalk.SpecFlow.Bindings;


public static class DynamicClassEmitter
{
    public static object ToAnonymousObject(this IDictionary<string, object> dict)
    {
        var type = EmitClassFromDictionary(dict);
        var instance = Activator.CreateInstance(type);

        foreach (var kvp in dict)
        {
            var property = type.GetProperty(kvp.Key);
            property.SetValue(instance, kvp.Value, null);
        }

        return instance;
    }

    private static int _counter = 0;
    public static Type EmitClassFromDictionary(IDictionary<string, object> dictionary)
    {
        var assemblyName = new AssemblyName("DynamicAssembly_" + Interlocked.Increment(ref _counter));
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        // Define a public class named 'DynamicClass'.
        var typeBuilder = moduleBuilder.DefineType("DynamicClass", TypeAttributes.Public);

        // For each key-value pair in the dictionary, define a public property.
        foreach (var kvp in dictionary)
        {
            var propertyName = kvp.Key;
            var propertyType = kvp.Value.GetType();

            // Define a private field.
            var fieldBuilder = typeBuilder.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);
            // Define the property.
            var propertyBuilder = typeBuilder.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);

            // Define the 'get' accessor for the property.
            var getMethodBuilder = typeBuilder.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
            var getIlGenerator = getMethodBuilder.GetILGenerator();
            getIlGenerator.Emit(OpCodes.Ldarg_0);
            getIlGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
            getIlGenerator.Emit(OpCodes.Ret);
            propertyBuilder.SetGetMethod(getMethodBuilder);

            // Define the 'set' accessor for the property.
            var setMethodBuilder = typeBuilder.DefineMethod("set_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null, new Type[] { propertyType });
            var setIlGenerator = setMethodBuilder.GetILGenerator();
            setIlGenerator.Emit(OpCodes.Ldarg_0);
            setIlGenerator.Emit(OpCodes.Ldarg_1);
            setIlGenerator.Emit(OpCodes.Stfld, fieldBuilder);
            setIlGenerator.Emit(OpCodes.Ret);
            propertyBuilder.SetSetMethod(setMethodBuilder);
        }

        // Create the type.
        var resultType = typeBuilder.CreateTypeInfo().AsType();
        return resultType;
    }
}