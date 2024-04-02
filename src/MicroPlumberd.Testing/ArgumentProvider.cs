using System.Text.Json;
using TechTalk.SpecFlow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

class ArgumentProvider : IArgumentProvider
{
    public T RecognizeFromYaml<T>(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)  // see height_in_inches in sample yml 
            .Build();
        var obj = deserializer.Deserialize(yaml);
        var json = JsonSerializer.Serialize(obj);

        var ev = JsonSerializer.Deserialize<T>(json);
        return ev;
    }

    public T RecognizeFromTable<T>(Table table)
    {
        var obj = ToDictionary(table);
        return RecognizeDictionary<T>(obj);
    }

    private static T RecognizeDictionary<T>(IDictionary<string, object> obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<T>(json);
    }

    public record Item<T>(Guid Id, T Data) : IItem<T>;
    public IReadOnlyList<IItem<T>> RecognizeManyFromTable<T>(Table table)
    {
        var props = table.Header.Except(["Id"]).ToArray();
        var result = new List<IItem<T>>();
        foreach (var r in table.Rows)
        {
            Guid id = Guid.NewGuid();
            if (table.ContainsColumn("Id"))
                id = Guid.TryParse(r["Id"], out var i) ? i : r["Id"].ToGuid();
            var data = props.ToDictionary(h => h, h => (object)r[h]);
            var tmp = new Item<T>(id, RecognizeDictionary<T>(data));
            result.Add(tmp);
        }

        return result;
    }

    private IDictionary<string, object> ToDictionary(Table table)
    {
        Dictionary<string, object> ret = new();
        if (table.Header.Count == 2 && table.Header.Contains("Property") && table.Header.Contains("Value"))
        {
            foreach (var i in table.Rows)
            {
                var prop = i["Property"];
                var value = i["Value"];
                ret.Add(prop, value);
            }
        }
        else if (table.RowCount == 1)
        {
            foreach (var i in table.Header)
            {
                ret.Add(i, table.Rows[0][i]);
            }
        }

        return ret;
    }
    public T Recognize<T>(object s) =>
        s switch
        {
            string s1 when !string.IsNullOrWhiteSpace(s1) => RecognizeFromYaml<T>(s1),
            Table t => RecognizeFromTable<T>(t),
            _ => default
        };
    public object Recognize(object s) =>
        s switch
        {
            string s1 when !string.IsNullOrWhiteSpace(s1) => Recognize(s1),
            Table t => Recognize(t),
            _ => default
        };

    public object Recognize(Table table)
    {
        var dict = ToDictionary(table);
        var anonymous = dict.ToAnonymousObject();
        return anonymous;
    }
    public object Recognize(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)  // see height_in_inches in sample yml 
            .Build();
        var dict = deserializer.Deserialize<Dictionary<string,object>>(yaml);
        var anonymous = dict.ToAnonymousObject();
        return anonymous;
    }
}