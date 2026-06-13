# Novel Script Language

Scripts are deliberately small and deterministic. A node script runs when the
node is entered. An output script runs after a choice is selected and before
the target node is entered.

## Commands

```text
# comment
set met_alice = true
set player_name = "Макс"
set score = 2
add score 1
unset temporary_flag
```

Variable names use Latin letters, digits, and `_`, and cannot start with a
digit. Values may be strings, numbers, booleans, or `null`.

## Choice conditions

```text
met_alice
!door_locked
score >= 3
player_name == "Макс"
route != "bad"
```

An empty condition means that the choice is always available.
