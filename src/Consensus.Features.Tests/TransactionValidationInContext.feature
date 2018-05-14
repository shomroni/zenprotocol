Feature: Transaction validation with utxo-set context

  Scenario: A transaction gets validated, result is Ok
    Given utxoset
      | Tx   | Key  | Asset | Amount |
      | tx-1 | key1 | Zen   | 100    |
      | tx-2 | key2 | Zen   |  20    |
    And tx1 locks 120 Zen to key3
    And tx1 has the input tx-1 index 0
    And tx1 has the input tx-2 index 0
    When tx1 is signed with key1,key2
    Then tx1 should pass validation

  Scenario: A contract is executed and the result is validated, result is Ok
    Given utxoset
      | Tx   | Key  | Asset | Amount |
      | tx-1 | key1 | Zen   | 100    |
    And tx1 locks 100 Zen to key3
    And tx1 has the input tx-1 index 0
    When c1 executes on tx1 returning tx2
    When tx2 is signed with key1
    Then tx2 should pass validation
    And tx2 should lock 1000 c1 to c1
    