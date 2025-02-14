namespace MicroPlumberd.Services;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple=true)]
public class ThrowsFaultExceptionAttribute<TMessage>() : ThrowsFaultExceptionAttribute(typeof(TMessage));