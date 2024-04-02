using TechTalk.SpecFlow;

public interface IArgumentProvider
{
    T RecognizeFromYaml<T>(string yaml);
    T RecognizeFromTable<T>(Table table);
    IReadOnlyList<IItem<T>> RecognizeManyFromTable<T>(Table table);
    T Recognize<T>(object arg);
    object Recognize(Table table);
    object Recognize(string yaml);
    object Recognize(object s);
}