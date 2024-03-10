namespace MicroPlumberd;

public static class Serializer
{
    public static IObjectSerializer Instance { get; set; } =new ObjectSerializer();
}