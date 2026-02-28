---
source_beads:
  - id: TazUo-mol-1vj
    title: "Update TryRemoveAutoLootEntry and remove legacy methods"
    has_learnings: false
    has_gaps: true
  - id: TazUo-mol-604
    title: "Update AddAutoLootEntry to route to SelectedProfile"
    has_learnings: false
    has_gaps: true
  - id: TazUo-mol-87b
    title: "Implement CreateProfile method"
    has_learnings: false
    has_gaps: true
  - id: TazUo-mol-1rs
    title: "Add _mergedEntries and implement RebuildMergedList"
    has_learnings: false
    has_gaps: true
  - id: TazUo-mol-gn7
    title: "Update ImportFromOtherCharacter to create profile"
    has_learnings: false
    has_gaps: true
gaps_to_review:
  - bead_id: TazUo-mol-87b,TazUo-mol-1rs,TazUo-mol-gn7
    gap: "Thread safety for Profiles list and Profile.Entries"
    context: "AutoLootManager.cs - Profiles list and individual Entries lists accessed from UI and background threads without synchronization"
    action: pending
  - bead_id: TazUo-mol-604
    gap: "Null return handling in AddAutoLootEntry callers"
    context: "NearbyLootGump.cs:464 and GridContainer.cs:1383 don't check null return"
    action: pending
  - bead_id: TazUo-mol-1vj
    gap: "_savePath field cleanup"
    context: "_savePath still exists for migration detection but old Load/Save are gone"
    action: pending
skills_to_create: []
skills_to_modify: []
artifacts_to_update: []
created: 2026-02-11
---

# Harvest Plan

Found **0 learnings** and **6 gap comments** from 5 completed tasks. After deduplication: **3 unique gaps** to review. No documentation artifacts proposed (no learnings to capture).

## Existing Documentation

- **CLAUDE.md** (root) - Project overview, build instructions, architecture, conventions

No skills directory exists. No folder-level CLAUDE.md files.

## Gaps to Review

### 1. Thread safety for Profiles list and Profile.Entries

**Source beads**: TazUo-mol-87b, TazUo-mol-1rs, TazUo-mol-gn7 (mentioned 4 times across 3 beads)
**Context**: `AutoLootManager.cs` - The `Profiles` list (`List<AutoLootProfile>`) is mutated by UI thread (CreateProfile, DeleteProfile, RenameProfile, ReorderProfile, FinalizeImportedProfile, ImportFromOtherCharacter) while `RebuildMergedList` iterates it. Individual `Profile.Entries` lists can be mutated by UI while `RebuildMergedList` iterates them. Pre-existing architectural concern.
**Severity**: medium (no crashes reported yet, but theoretically possible under heavy concurrent access)

#### Proposed Task

**Title**: Add thread safety for AutoLootManager Profiles and Entries access
**Category**: infrastructure
**Priority**: P3

#### Existing Coverage Check

No existing open beads cover this.

#### Review Notes

<!--
Set action to: approved | rejected
Add your review comments here
-->

---

### 2. Null return handling in AddAutoLootEntry callers

**Source bead**: TazUo-mol-604
**Context**: `NearbyLootGump.cs:464` and `GridContainer.cs:1383` call `AddAutoLootEntry()` but don't check for null return. They print "Added this item to auto loot" even when the method returns null (e.g., when no profile is selected).
**Severity**: low (user sees misleading success message but no data corruption)

#### Proposed Task

**Title**: Add null-check for AddAutoLootEntry return in NearbyLootGump and GridContainer
**Category**: style
**Priority**: P3

#### Existing Coverage Check

No existing open beads cover this.

#### Review Notes

<!--
Set action to: approved | rejected
Add your review comments here
-->

---

### 3. _savePath field cleanup

**Source bead**: TazUo-mol-1vj
**Context**: The `_savePath` field was originally used by legacy `Load()` and `Save()` methods. Those methods have been replaced by the profile system. `_savePath` now only exists for migration detection (`LoadProfiles` checks if `AutoLoot.json` exists at that path). Could be replaced with an inline `Path.Combine()` call and the field removed.
**Severity**: low (dead weight, not a bug)

#### Proposed Task

**Title**: Remove _savePath field and inline migration path in LoadProfiles
**Category**: style
**Priority**: P4

#### Existing Coverage Check

Migration tasks TazUo-mol-8kx and TazUo-mol-hi9 are both closed. The field is only used in one place now.

#### Review Notes

<!--
Set action to: approved | rejected
Add your review comments here
-->

---

## Proposed Artifacts

None. No `[LEARNING]` comments were found across any completed beads.

## Skipped Learnings

No learnings were found.

## Skipped Gaps

| Bead | Gap | Reason |
|------|-----|--------|
| TazUo-mol-1rs | Thread safety of Profile.Entries | Merged into Gap #1 (same root cause) |
| TazUo-mol-gn7 | Thread safety on Profiles list | Merged into Gap #1 (duplicate) |

## Next Steps

1. Review each gap above and set `action` to `approved` or `rejected` in frontmatter
2. Add comments in "Review Notes" sections for any changes
3. Run `/harvest` again to create tasks for approved gaps
