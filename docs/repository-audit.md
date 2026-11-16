# Repository setup and transfer

Best-effort application of `microsoft/amplifier/recipes/repo-audit.yaml` v1.11.0.
This is a manual setup record, not a claim that the recipe runner has executed.
Reference boilerplate is `microsoft/amplifier-core` main, fetched 2026-11-16.

## Applied baseline

- Canonical CODE_OF_CONDUCT.md, SECURITY.md, SUPPORT.md and MIT LICENSE.
- Canonical README Contributing and Trademarks sections, with a separate truthful
  note that the personal repository does not have Microsoft CLA automation.
- Product vision, platform contract, contribution guidance and agent instructions.
- Public source only; private qualification receipts stay outside this repository.
- Issues, wiki and projects disabled, following the audit's ecosystem defaults.
- Squash merges only; obsolete PR branches deleted after merge.
- Main ruleset: prevent deletion/force pushes, linear history, one approving review,
  stale-review dismissal, code-owner review and resolved review discussions.
- Admin bypass is restricted to PR merges, matching the recipe; it is not permission
  for an agent to bypass review without a specific user instruction.

## Transfer follow-ups

The personal repository cannot assign Microsoft's maintainer team or enroll itself
through the organization's repository registration process. After transfer:

1. Complete the owner's Microsoft registration and CLA bot setup.
2. Set CODEOWNERS to the authorized Microsoft maintainer team, and add that verified
   team to the ruleset with `bypass_mode: pull_request`; retain existing checks.
3. Add the accepted product repository to `microsoft/amplifier/docs/MODULES.md` and
   update canonical client ownership links in Unified.
4. Verify Actions permissions, secret scanning, dependency alerts and required CI
   checks under the destination organization's policy; run the actual audit recipe.
5. Set up Windows publisher signing and protected release approval. Qualify the
   exact signed installer/update artifacts before any end-user release.

No organization permissions, CLA service, signing identity or release qualification
are implied by copying the standard files. No signing credentials are stored here.
