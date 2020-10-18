Feature: Active Contract Set

  Scenario: A contract is activated
    Given genesisTx locks 10 Zen to key1
    And genesis has genesisTx
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1000 blocks
    And activationTx locks change Zen to activationTxChangeKey
    When signing activationTx with key1
    And validating a block containing activationTx extending tip
    Then c1 should be active for 999 blocks

  Scenario: A contract is reactivated
    Given genesisTx locks 10 Zen to key1
    And genesis has genesisTx
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1 block
    And activationTx locks change Zen to activationTxChangeKey
    And activationTx is signed with key1
    When validating a block containing activationTx extending tip
    Then c1 should not be active

    Given reactivationTx has the input activationTx index 2
    And reactivationTx activates c1 for 2 block
    And reactivationTx locks change Zen to activationTxChangeKey1
    And reactivationTx is signed with activationTxChangeKey
    When validating block bkMain1 containing reactivationTx extending tip
    Then tip should be bkMain1
    And c1 should be active for 1 block

  Scenario: An expired contract is not extended
    Given genesisTx locks 10 Zen to key1
    And genesis has genesisTx
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1 block
    And activationTx locks change Zen to activationTxChangeKey
    And activationTx is signed with key1
    When validating block bkMain containing activationTx extending tip
    Then c1 should not be active

    Given extensionTx has the input activationTx index 2
    And extensionTx extends c1 for 20 block
    And extensionTx locks change Zen to extensionTxChangeKey1
    And extensionTx is signed with activationTxChangeKey
    When validating block bkMain1 containing extensionTx extending tip
    # block should not be accepted since the contract is expired
    Then tip should be bkMain
    And c1 should not be active

  Scenario: A contract is extended
    Given genesisTx locks 10 Zen to key1
    And genesis has genesisTx
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 2 block
    And activationTx locks change Zen to activationTxChangeKey
    And activationTx is signed with key1
    When validating block bkMain containing activationTx extending tip
    Then c1 should be active for 1 block

    Given extensionTx has the input activationTx index 2
    And extensionTx extends c1 for 20 block
    And extensionTx locks change Zen to extensionTxChangeKey1
    And extensionTx is signed with activationTxChangeKey
    When validating block bkMain1 containing extensionTx extending tip
    Then tip should be bkMain1
    And c1 should be active for?

  Scenario: A contract is activated and executed in main chain
    Given genesisTx locks 10 Zen to key1
    And genesisTx locks 10 Zen to key2
    And genesis has genesisTx
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1000 blocks
    And activationTx locks change Zen to activationTxChangeKey
    When signing activationTx with key1
    And validating a block containing activationTx extending tip
    Then c1 should be active for 999 blocks

    Given txMain locks 10 Zen to mainKey3
    And txMain has the input genesisTx index 1
    When executing c1 on txMain returning txMainOfContract
    And signing txMainOfContract with key2
    And validating block bkMain containing txMainOfContract extending tip
    Then tip should be bkMain

  Scenario: Complex reorgs

    Given genesisTx locks 10 Zen to key1
    And genesisTx locks 10 Zen to key2
    And genesisTx locks 10 Zen to key2
    And genesisTx locks 10 Zen to key3
    And genesisTx locks 10 Zen to key4
    And genesis has genesisTx
    
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1000 blocks
    And activationTx locks change Zen to activationTxChangeKey
    When signing activationTx with key1
    And validating block b1 containing activationTx extending tip
    Then c1 should be active for 999 blocks

    Given txMain locks 10 Zen to mainKey3
    And txMain has the input genesisTx index 1
    When executing c1 on txMain returning txMainOfContract
    And signing txMainOfContract with key2
    And validating block bkMain containing txMainOfContract extending b1
    
    Given txMainBis locks 10 Zen to mainBisKey3
    And txMainBis has the input genesisTx index 2 
    When executing c1 on txMainBis returning txMainBisOfContract
    And signing txMainBisOfContract with key2

    Given tx2 has the input txMainBisOfContract index 0
    And tx2 locks 10 Zen to tx2_key1
    When signing tx2 with mainBisKey3

    When validating block mainChainBlock containing txMainOfContract,txMainBisOfContract,tx2 extending b1
    
    Given tx3 has the input genesisTx index 4
    And tx3 locks 10 Zen to tx3_key1
    When signing tx3 with key4

    And validating block mainTip containing tx3 extending mainChainBlock

    Then tip should be mainTip

  Scenario: A contract becomes deactivated on a reorg
    Given genesisTx locks 10 Zen to key1
    And genesis has genesisTx
    
    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1000 blocks
    And activationTx locks change Zen to activationTxChangeKey
    When signing activationTx with key1
    And validating block bkMain1 containing activationTx extending tip
    Then c1 should be active for 999 blocks

    When validating an empty block bkSide1 extending genesis
    Then tip should be bkMain1
    And c1 should be active for 999 blocks
    
    When validating an empty block bkSide2 extending bkSide1
    Then tip should be bkSide2
    And c1 should not be active

  Scenario: A contract becomes reactivated on a second reorg
    Given genesisTx locks 10 Zen to key1
    And genesis has genesisTx

    And activationTx has the input genesisTx index 0
    And activationTx activates c1 for 1000 blocks
    And activationTx locks change Zen to activationTxChangeKey
    When signing activationTx with key1
    And validating block bkMain1 containing activationTx extending tip
    Then c1 should be active for 999 blocks

    When validating an empty block bkSide1 extending genesis
    Then tip should be bkMain1
    And c1 should be active for 999 blocks

    When validating an empty block bkSide2 extending bkSide1
    Then tip should be bkSide2
    And c1 should not be active

    When validating an empty block bkMain2 extending bkMain1
    Then tip should be bkSide2
    And c1 should not be active

    When validating an empty block bkMain3 extending bkMain2
    Then tip should be bkMain3
    And c1 should be active for 997 blocks
