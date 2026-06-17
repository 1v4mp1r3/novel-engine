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
toggle met_alice
```

Variable names use Latin letters, digits, and `_`, and cannot start with a
digit. Values may be strings, numbers, booleans, or `null`.
`toggle name` flips a variable through the same truthiness rules used by
conditions; a missing variable becomes `true`.
The visual-block editor can import these simple commands from text scripts:
comments, `set`, `add`, `unset`, and `toggle`. Unsupported commands stay
text-only until the block system grows matching block types. After an import,
the editor can clear the original text script so the same commands are not
executed twice.

## Choice conditions

```text
met_alice
!door_locked
score >= 3
player_name == "Макс"
route != "bad"
met_alice && score >= 3
route == "good" || route == "true"
(met_alice || route == "good") && score >= 3
```

An empty condition means that the choice is always available.
Use `&&` for AND and `||` for OR. AND is evaluated before OR; use parentheses
when a branch needs an OR group inside an AND group.
The condition builder can assemble AND/OR groups visually and stores them in the
same core condition model used by hand-written DSL.
