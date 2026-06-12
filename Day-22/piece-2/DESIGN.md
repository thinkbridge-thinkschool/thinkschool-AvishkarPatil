# Bus Booking — One-Page Design

## Product Slice
Online bus ticket booking: search routes, select seats, book tickets, and receive confirmation — all as a single deployable modular monolith.

---

## Bounded Contexts

### 1. Scheduling
**Owns:** Routes, Buses, Schedules, Seats  
**Responsibility:** What buses run, when, on which routes, at what seat prices. Manages seat inventory and the 10-minute reservation lock.  
**Key invariant:** A seat can only be reserved if its status is `Available`. Optimistic concurrency (`RowVersion`) prevents double-booking under concurrent writes.

### 2. Booking
**Owns:** Booking aggregate, BookedSeat (value object)  
**Responsibility:** Orchestrates the booking lifecycle — Pending → Confirmed → Cancelled/Completed. Captures a price snapshot at booking time. Raises domain events that cross context boundaries.  
**Key invariant:** A booking must have at least one seat. Cancellation is forbidden once Completed.

### 3. Notifications *(consumer only — no persistence)*
**Owns:** Nothing persisted  
**Responsibility:** Consumes `BookingConfirmedEvent` from Service Bus and sends a ticket confirmation email. Stateless — retry is handled by Service Bus dead-letter queue.

---

## Core Aggregate: `Booking`

```
Booking (Aggregate Root)
├── Id: Guid
├── UserId: Guid          — reference, not navigation
├── UserEmail: string
├── ScheduleId: Guid      — reference, not navigation
├── Status: BookingStatus  [Pending → Confirmed → Cancelled | Completed]
├── TotalAmount: decimal   — sum of seat price snapshots
├── BookedAt: DateTime
├── Seats: List<BookedSeat>  (owned JSON collection in EF)
│   └── BookedSeat: record(SeatNumber, PassengerName, PassengerAge, PassengerGender, SeatPrice)
│
└── Domain methods
    ├── Create(userId, email, scheduleId, seats) → Booking
    ├── Confirm(userName) → raises BookingConfirmedEvent
    ├── Cancel()          → raises BookingCancelledEvent
    └── Complete()
```

State machine:
```
[Pending] --Confirm()--> [Confirmed] --Complete()--> [Completed]
    |                        |
    └───Cancel()─────────────┘
             ↓
         [Cancelled]
```

---

## Async Flows

```
 Booking Context                   Service Bus                 Consumer
 ───────────────                   ───────────                 ────────

 booking.Confirm()
   → raises BookingConfirmedEvent
       │
       ▼
 ServiceBusEventPublisher          topic: booking-confirmed    Notifications context
 serialises + sends msg ──────────────────────────────────►   sends ticket email to UserEmail

 booking.Cancel()
   → raises BookingCancelledEvent
       │
       ▼
 ServiceBusEventPublisher          topic: booking-cancelled    Scheduling context
 serialises + sends msg ──────────────────────────────────►   releases seats (calls seat.Release()
                                                               for each ReleasedSeatNumber)

 BackgroundService (every 5 min)
   SeatExpiryService scans all
   active schedules, calls
   seat.Release() on expired        [in-process, no bus]
   reservations (>10 min locked)
```

**Why Service Bus for cross-context events?**  
The Booking context must not directly call the Scheduling or Notifications context — that would couple them at deploy time. Service Bus provides at-least-once delivery with DLQ for failed consumers, matching the Outbox pattern already established on Day 20.

---

## Solution Layout

```
BusBooking.sln
├── src/
│   ├── BusBooking.Domain/              — no external dependencies
│   │   ├── Common/                     BaseEntity, IDomainEvent
│   │   ├── Scheduling/
│   │   │   ├── Entities/               Route, Bus, Schedule, Seat
│   │   │   └── Enums/                  BusType, SeatType, SeatStatus
│   │   └── Booking/
│   │       ├── Aggregates/             Booking  ← core aggregate
│   │       ├── ValueObjects/           BookedSeat
│   │       ├── Enums/                  BookingStatus
│   │       └── Events/                 BookingConfirmedEvent, BookingCancelledEvent
│   │
│   ├── BusBooking.Application/         → depends on Domain only
│   │   ├── Common/                     IEventPublisher, NotFoundException
│   │   ├── Scheduling/
│   │   │   ├── Queries/SearchSchedules/
│   │   │   └── Repositories/          IScheduleRepository
│   │   └── Booking/
│   │       ├── Commands/CreateBooking/
│   │       ├── Commands/CancelBooking/
│   │       ├── Queries/GetUserBookings/
│   │       └── Repositories/          IBookingRepository
│   │
│   ├── BusBooking.Infrastructure/      → depends on Application + Domain
│   │   ├── Persistence/               BusBookingDbContext + EF Configurations
│   │   ├── Repositories/              BookingRepository, ScheduleRepository
│   │   ├── Messaging/                 ServiceBusEventPublisher
│   │   ├── BackgroundServices/        SeatExpiryService (IHostedService)
│   │   └── InfrastructureServiceExtensions.cs
│   │
│   └── BusBooking.Api/                → depends on Application + Infrastructure
│       ├── Booking/                   BookingEndpoints (Minimal API)
│       ├── Scheduling/                ScheduleEndpoints (Minimal API)
│       └── Program.cs
│
└── tests/
    └── BusBooking.Domain.Tests/        BookingAggregateTests (5 cases)
```

---

## Dependency Rule (enforced by project references)

```
Api → Infrastructure → Application → Domain
                    ↗
      Infrastructure
```

Domain has zero dependencies. Application depends only on Domain. Infrastructure implements Application interfaces. Api wires it all together.

---

## Key Design Decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Modular Monolith | One deployable; bounded by namespace not process |
| Seat concurrency | EF `RowVersion` on `Seat` | Prevents double-booking without distributed lock |
| Payment | Stubbed (`Confirm()` auto-succeeds) | Out of scope for capstone; extractable later |
| Cross-context events | Azure Service Bus | At-least-once delivery + DLQ; consistent with Day 19-20 patterns |
| Seat expiry | `BackgroundService` every 5 min | Replaces Spring's `@Scheduled`; no external scheduler needed |
| DTO storage | Owned JSON collection (`BookedSeat`) | Price snapshot frozen at booking time; no FK join needed |
