Feature: CommandHandler
	Simple command handler that uses a Foo aggregate and Command Bus.

Background: 
	Given the Foo App is up and running
		
Scenario: Invoking one command
	Given Foo 'Orange' was created
	  | Name |
	  | Foo  |
    When I change foo 'Orange' with:
      | Name   |
      | Blabla |
	Then I expect, that Foo was refined with:
	  | Name   |
	  | Blabla | 
    And I expect, that Foo's state is set with:
      | Name   |
      | Blabla | 
     
Scenario: Invoking one command to raise exception
	Given Foo was created
	  | Name |
	  | Foo  |
	When I change foo with:
	  | Name  |
	  | error |
	Then I expect business fault exception:
	  | Name                       |
	  | Houston we have a problem! | 