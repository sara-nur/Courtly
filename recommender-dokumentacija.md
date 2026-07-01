# Courtly — Recommender documentation

This document describes the Courtly court-recommendation system exactly as it is implemented. It is kept in
lockstep with the code; the authoritative sources are:

- `backend/src/Courtly.Application/Recommendations/RecommendationService.cs` — the algorithm.
- `backend/src/Courtly.Application/Recommendations/RecommendationWeights.cs` — every weight/constant.
- `backend/src/Courtly.Api/Controllers/RecommendationsController.cs` — the HTTP surface.
- `backend/src/Courtly.Domain/Entities/RecommendationFeedback.cs` + its EF configuration — feedback storage.

## 1. Approach

Courtly uses a **content-based** recommender with a **popularity** fallback. For each user it builds a
preference profile from signals the app really records, scores every bookable court against that profile,
blends in a popularity component, applies the user's explicit Yes/No feedback, and returns a ranked list where
**each court carries a human-readable reason** ("Because you like Clay", "☀️ Morning availability", …). There is
no trained/opaque model: scoring is a transparent weighted sum whose weights are versioned constants, so the
behaviour is deterministic and explainable end-to-end.

## 2. Signals (all really written in the app)

| Signal | Source table | Where it is written |
|---|---|---|
| Surface, court type, price, indoor preference | `Reservations` (non-cancelled) → `Court` | Created by the booking flow (F14/F25) |
| Time-of-day preference (Morning/Afternoon/Evening) | `Reservations` → `TimeSlot.Bucket` | Same booking flow |
| Searched surface / court type / price band / indoor | `SearchHistory` | Written on every explicit search (F23) |
| Explicit "was this helpful?" | `RecommendationFeedback` | Written by the Yes/No control (F29) |

`SearchHistory.Bucket` is intentionally never captured (the client does not send a time bucket on search), so
the **time-of-day preference is derived only from bookings**. Every signal that participates in scoring is used;
none is collected and ignored.

## 3. Preference profile

Built per user (see `BuildProfileAsync`). Let a **booking** count as weight `2.0` and a **search** as weight
`1.0` (`BookingSignalWeight` / `SearchSignalWeight`).

- **Surface weights** — for each surface, sum `2 × (bookings on that surface) + 1 × (searches for that surface)`,
  then normalize so the weights sum to 1.
- **Court-type weights** — same construction over court type.
- **Time-bucket weights** — from bookings only (`Reservation → TimeSlot.Bucket`), normalized; the highest becomes
  the user's **top bucket**.
- **Target price** — the **median** of {booked courts' hourly prices} ∪ {midpoint of each search's min/max price
  band, or the single provided bound}. If there is no price signal, the price term is disabled.
- **Indoor preference** — the share of indoor among all indoor-bearing signals (bookings' `IsIndoor` + searches'
  `IndoorOnly`), in `[0,1]`. If there is no indoor signal, the indoor term is disabled.

Every normalization guards against a zero denominator (an empty distribution yields no contribution rather than
`NaN`).

**Cold-start:** if the user has **no** bookings and **no** searches, the profile is flagged cold-start and
scoring uses the popularity component only.

## 4. Candidate set

Courts that are `IsActive = true` **and not under an open maintenance window** (a `CourtMaintenanceLog` that is
`Scheduled`/`InProgress` and covers "now" — the same F12 predicate used across the app). Ordered `IsFeatured`
first, then by id, and capped at **`CandidateCap = 200`** to bound the read and the in-memory scoring pass. Each
candidate is projected (one `AsNoTracking` query) to its surface/court-type/indoor/featured/price, its average
rating + review count, and its completed-reservation count.

## 5. Scoring

All content terms are in `[0,1]` and are multiplied by their weight (a disabled term contributes 0).

```
content    = wSurface   · surfaceWeight[court.surface]
           + wCourtType · courtTypeWeight[court.courtType]
           + wBucket    · (court has an upcoming active slot in the user's top bucket ? 1 : 0)
           + wPrice     · priceProximity(court.price, targetPrice)
           + wIndoor    · (court.isIndoor ? indoorPref : 1 − indoorPref)

popularity = wPopReservations · norm(completedReservations, maxOverCandidates)
           + wPopRating       · ratingNorm            // ratingNorm = reviews==0 ? 0 : (avgRating − 1) / 4

score      = wContentGroup · content + wPopularityGroup · popularity + feedbackAdjustment
           (cold-start: score = popularity + feedbackAdjustment)
```

- `norm(x, max) = max ≤ 0 ? 0 : x / max` (per-batch min-max, guarded).
- **Price proximity** is a Gaussian: `priceProximity(p, t) = exp( −(p − t)² / (2·σ²) )`, with
  `σ = max(t · SigmaFactor, SigmaFloor)` = `max(target·0.25, 5)`. It is 1.0 at the target price and decays with
  distance; the floor prevents cheap courts from being hypersensitive to small absolute gaps.
- **Upcoming top-bucket availability** is resolved with a single grouped query over `TimeSlots` (active, starting
  in the future, in the top bucket) — never a per-court query.

### Weights (from `RecommendationWeights.cs`)

| Constant | Value | Meaning |
|---|---|---|
| `ContentGroup` | 0.75 | content share of the final score |
| `PopularityGroup` | 0.25 | popularity share |
| `Surface` | 0.35 | content: surface match |
| `Bucket` | 0.25 | content: preferred time-of-day availability |
| `CourtType` | 0.15 | content: court-type match |
| `Price` | 0.15 | content: price proximity |
| `Indoor` | 0.10 | content: indoor/outdoor match |
| `PopReservations` | 0.60 | popularity: completed-booking volume |
| `PopRating` | 0.40 | popularity: average rating |
| `FeedbackNegative` | −1.0 | a "No" — demotes the court below every non-disliked one (§7) |
| `FeedbackPositive` | +0.05 | a "Yes" nudge |
| `BookingSignalWeight` / `SearchSignalWeight` | 2.0 / 1.0 | booking vs search signal strength |
| `SigmaFactor` / `SigmaFloor` | 0.25 / 5.0 | price-proximity tolerance |
| `CandidateCap` | 200 | max courts scored |

Surface (0.35) + time-bucket (0.25) dominate the content component, so they are the visible drivers of the
grouped UI, matching the product mockup.

Ranking is by score descending, with a deterministic tie-break: featured first, then higher average rating, then
lowest id.

## 6. Explainable reason

For each recommended court the reason is the **single strongest enabled content dimension** (argmax of the
weighted contributions); ties resolve in the order Surface → TimeBucket → Price → Indoor → CourtType. When no
content dimension applies (cold-start, or a court that matches nothing) the reason is a popularity one:
**Popular** if the court has completed bookings, otherwise **Top rated**. Reason codes and their strings:

| Reason code | Example string |
|---|---|
| Surface | "Because you like Clay" |
| TimeBucket | "☀️ Morning availability" |
| Price | "Around your usual price" |
| Indoor | "Indoor courts you prefer" / "Outdoor courts you prefer" |
| CourtType | "More Singles courts" |
| Popular | "Popular right now" |
| TopRated | "Highly rated" |

The client groups the ranked list by this reason string into the sections shown on the recommendations screen.

## 7. Feedback loop (Yes/No — stored and used)

Each recommended court card carries its own 👍/👎, so feedback is **per court** (not a single verdict on the whole
list). Tapping a thumb POSTs `{ isHelpful, courtIds }` for that one court; the server upserts one
`RecommendationFeedback` row **per (user, court)** (a unique index enforces one row; the service loads existing
rows and updates-or-inserts, then saves once). The endpoint accepts a list of court ids, so a batch is possible,
but the UI sends a single court. On the next fetch:

- **No** → that court is **demoted**: the `FeedbackNegative` (−1.0) term pushes it below every court the user has
  *not* disliked, so it sinks to the bottom (it is **not** removed — the list can never empty out, even if the
  user dislikes every court).
- **Yes** → those courts receive the small `FeedbackPositive` boost and remain near the top.

Feedback is read **fresh on every recommendation request**, so a No/Yes takes effect on the very next fetch.

## 8. Configuration & model management

There is no learned model to persist. The "model" is the set of weights in `RecommendationWeights.cs`
(compiled constants — one source of truth, matching this document). Tuning the recommender means changing those
constants; nothing is stored per-deployment. The recommendation is **computed fresh on every request** — the
per-user preference profile is deliberately **not** cached, because it is derived from mutable booking state and
must reflect a new booking (or a new Yes/No) immediately; a TTL cache would make those lag. The reads are a
handful of bounded, `AsNoTracking`, set-based queries (no N+1), and the candidate set is capped at
`CandidateCap` (200), so recomputing per request is cheap. (Caching in Courtly is applied where it belongs — the
heavy shared dashboard aggregates and the auth token denylist.)

## 9. HTTP endpoints

Both require a valid JWT; the user is always taken from the token, never the request body.

- `GET /api/recommendations?page&pageSize` → `{ summary: { message, isContentBased, basedOnBookings },
  page: PagedResult<{ court: CourtDto, score, reasonCode, reason, userFeedback }> }`. `userFeedback` is the
  caller's current per-court rating (null / true / false) so the client can highlight the active thumb. Paginated
  with the standard max page size of 100. `isContentBased` is false on cold-start.
- `POST /api/recommendations/feedback` with `{ isHelpful: bool, courtIds: number[] }` → `204 No Content`.
  Validated server-side: at least one court, at most 100 courts.

## 10. How to verify

1. Sign in as the seeded mobile user (`mobile` / `test`), who already has bookings.
2. `GET /api/recommendations` — the response ranks courts with reasons drawn from the user's booked surfaces /
   times, and `summary.isContentBased` is true.
3. Book an evening-clay pattern, then request again — clay / evening courts rank higher and their reasons reflect
   that.
4. `POST /api/recommendations/feedback` with `isHelpful=false` and a shown court id, then `GET` again — that
   court drops to the bottom of the ranking (a "No" demonstrably changed the results; it is demoted, not removed).
5. A brand-new user with no history gets a popularity-only list with `isContentBased=false`.
