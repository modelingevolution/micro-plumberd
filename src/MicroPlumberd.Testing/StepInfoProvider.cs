using TechTalk.SpecFlow;

class StepInfoProvider(ScenarioContext scenarioContext) : IStepInfoProvider
{
    public string CurrentStepName => scenarioContext.StepContext.StepInfo.Text;
    public Table Table => scenarioContext.StepContext.StepInfo.Table;
    public string Multiline => scenarioContext.StepContext.StepInfo.MultilineText;
}