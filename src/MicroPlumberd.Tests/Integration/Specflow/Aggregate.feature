@Aggregate
Feature: Foo aggregate flow

Background: 
    Given the Foo App is up and running
   
Scenario: Creating and changing Foo should be successful
    
    Given Foo created
    """
    Name: "Ok"
    """
    
    And Foo was updated:
        | Property | Value |
        | Name     | Ok    |

    When I change Foo with msg: 'Blabla'
    Then I expect, that Foo was updated with:
        | Name   |
        | Blabla | 

Scenario: Changing Foo with an error
    
    Given Foo was created
        | Name |
        | Foo  |

    When I change Foo with msg: 'error'
    Then I expect business fault exception:
        | Name                       |
        | Houston we have a problem! | 