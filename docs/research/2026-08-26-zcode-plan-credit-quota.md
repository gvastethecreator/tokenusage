# ZCode plan credit quota gate

Date: 2026-08-26

Decision: `retired` (shipped same day, then removed: the estimate does not
show the real remaining usage and no authorized source provides it)

## Retirement note

The maintainer removed the estimated credit bars and the plan-tier picker.
The formula research below stays for a future authorized contract. Hooks
remain installed as refresh triggers only; see
[hook payload research](2026-08-26-hook-payload-quota-research.md).

## Executive result

Z.ai publishes the GLM Coding Plan credit formula, the model multipliers, and
the tier limits. TokenUsage can estimate the 5-hour and weekly credit pools
from its own stored ZCode usage events.

The estimate is local. It makes no network call and reads no credential. Every
surface that shows it must carry an estimated label.

The server-side quota remains closed. The Coding Plan monitor endpoints are
still private and still need another tool's credential.

## Published inputs

Source: the official GLM Coding Plan overview page.

- Formula: model credits = (input tokens x input multiplier + cached input
  tokens x cached-input multiplier + output tokens x output multiplier)
  / 10,000.
- GLM-5.3 multipliers: input 6.9, cached input 1.7, output 24.
- GLM-5.3-Flash multipliers: input 2.3, cached input 0.56, output 8.
- Routing: GLM-5.2 and GLM-5.1 requests route to GLM-5.3. Turbo and 4.7
  requests route to GLM-5.3-Flash.
- Off-peak use bills at half rate. Peak runs Monday to Friday, 14:00 to 18:00
  Singapore time.
- Pools: Lite 2,000 credits per 5 hours and 10,000 per week. Pro 12,000 and
  60,000. Max 28,000 and 140,000.
- The 5-hour pool refills 5 hours after consumption. The weekly pool resets
  every 7 days.

## Estimation rules

The reader estimates from TokenUsage's own usage database, never from the
ZCode installation.

1. The person picks their plan tier in options. The tier is stored in
   `zcode-plan.v1.json` under the TokenUsage data folder.
2. The estimate sums credits over the stored ZCode events in a rolling 5-hour
   window and a rolling 7-day window.
3. Models without a published multiplier count as unmetered events. The
   estimate never invents a rate for them.
4. Cache writes have no published multiplier, so they bill at the input rate.
5. Unknown tier or missing data means no quota is shown. The estimate never
   turns into an invented zero.

## Known limits of the estimate

- MCP tool credits (web search, web reader, Zread at 1.2 credits per call) are
  not stored in the ZCode usage database. The estimate understates used
  credits by that amount.
- Plan use from other devices or other tools on the same plan is invisible to
  this machine.
- The server resets the 5-hour pool 5 hours after consumption starts. The
  rolling window is an approximation of that dynamic reset.
- A rate change on the Z.ai side applies only after the dated catalog version
  is updated here.

Because of these limits, the quota always shows as estimated, and the card
notice names the two biggest blind spots.

## Cost boundary

Plan credits are not money. This estimate never feeds the spend numbers. The
spend side keeps using the public Z.ai API rates as a separate, labeled
catalog estimate.

## Still blocked

- Any call to the Coding Plan monitor endpoints.
- Any reuse of ZCode or plugin credentials.
- Any claim of provider-reported quota for ZCode.

If Z.ai publishes an authorized quota API, the estimate retires in favor of
the reported values.

## Primary sources

- [GLM Coding Plan overview](https://docs.z.ai/devpack/overview)
- Local usage gate: [ZCode local usage database gate](2026-08-26-zcode-local-usage-source.md)
