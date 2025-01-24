using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Examples.Cinema.Scheduler;

public class SpaceArrayJsonConverter : JsonConverter<Space[,]>
{
    public override Space[,] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string");
        }
        string data = reader.GetString();
        string[] rows = data.Split('\n');
        int rowCount = rows.Length;
        int columnCount = rows[0].Length;
        var result = new Space[rowCount, columnCount];
        for (int i = 0; i < rowCount; i++)
        {
            for (int j = 0; j < columnCount; j++)
            {
                switch (rows[i][j])
                {
                    case 'x':
                        result[i, j] = Space.Used;
                        break;
                    case 'o':
                        result[i, j] = Space.Open;
                        break;
                    case '-':
                        result[i, j] = Space.Empty;
                        break;
                    default:
                        throw new JsonException("Unexpected character in input");
                }
            }
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, Space[,] value, JsonSerializerOptions options)
    {
        int rowCount = value.GetLength(0);
        int columnCount = value.GetLength(1);
        var result = new char[rowCount * (columnCount + 1) - 1];
        int index = 0;
        for (int i = 0; i < rowCount; i++)
        {
            for (int j = 0; j < columnCount; j++)
            {
                switch (value[i, j])
                {
                    case Space.Used:
                        result[index++] = 'x';
                        break;
                    case Space.Open:
                        result[index++] = 'o';
                        break;
                    case Space.Empty:
                        result[index++] = '-';
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected Space value");
                }
            }
            if (i < rowCount - 1)
            {
                result[index++] = '\n';
            }
        }
        writer.WriteStringValue(new string(result));
    }
}