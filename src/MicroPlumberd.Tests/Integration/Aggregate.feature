Feature: Foo flow

Background: 
    Given the app is up and running
    
Scenario: Calling an aggregate
    
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

Scenario: Calling an aggregate to throw exception
    
    Given Foo created
        | Name |
        | Foo  |

    When I change Foo with msg: 'error'
    Then I expect business fault exception:
        | Name   |
        | Houston we have a problem! | 