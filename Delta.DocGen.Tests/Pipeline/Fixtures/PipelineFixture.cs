namespace Delta.DocGen.Tests.Pipeline.Fixtures;

public static class PipelineFixture
{
    public static void WriteFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Auth"));
        Directory.CreateDirectory(Path.Combine(root, "Forms"));
        Directory.CreateDirectory(Path.Combine(root, "Features"));

        File.WriteAllText(Path.Combine(root, "Auth", "AuthSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class AuthSteps
            {
                [Given("I am logged in")]
                public void GivenLoggedIn() { }

                [When("I sign out")]
                public void WhenSignOut() { }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Forms", "FormSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class FormSteps
            {
                [Then("the form is submitted")]
                public void ThenFormSubmitted() { }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Features", "auth.feature"), """
            Feature: Auth
              Scenario: First
                Given I am logged in
                Then the form is submitted

              Scenario: Second
                Given I am logged in
                Then the form is submitted
            """);
    }
}
