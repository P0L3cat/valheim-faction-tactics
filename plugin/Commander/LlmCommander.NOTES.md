# LlmCommander (Route 3 — future only)

**Do not implement model code in this repository until explicitly approved.**

## Contract
- Implement `ICommander`.
- Input: `SquadSnapshot` (already JSON-friendly).
- Output: `SquadOrder` (same DTO as ScriptedCommander).
- On timeout / parse failure → fall back to `ScriptedCommander`.

## Constraints
- Dedicated server host only.
- Rate-limit proposes (≤ 1–2 Hz per squad).
- No per-frame inference; squad tick only.
- No shipping weights, tokenizers, or RPC clients in the plugin until green-lit.

## Suggested shape (pseudocode)
```
class LlmCommander : ICommander {
  ICommander Fallback; // ScriptedCommander
  SquadOrder? Propose(SquadSnapshot snap) {
    var json = Serialize(snap);
    var reply = InferOrRemote(json); // NOT IMPLEMENTED
    return TryParse(reply) ?? Fallback.Propose(snap);
  }
}
```
