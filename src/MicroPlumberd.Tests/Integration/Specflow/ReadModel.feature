Feature: ReadModel
	Simple model to query some data

Background: 
	Given the Foo App is up and running

Scenario: Foo model
	Given Some foos were created:
	  | Id | Name |
	  | 1  | Ok   |
	  | 2  | Oki  |
    When I find by id '1'
    Then I get 'Ok'
	
   