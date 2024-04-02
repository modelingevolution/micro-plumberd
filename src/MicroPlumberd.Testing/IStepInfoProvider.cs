using TechTalk.SpecFlow;

public interface IStepInfoProvider
{
    string CurrentStepName { get; }
    Table Table { get; }
    string Multiline { get; }
}