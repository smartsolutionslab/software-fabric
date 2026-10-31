## CQRS: none

- A module has one model: use cases read and write the same entities. A use case that changes state may return the changed data.
- Do not add read models or projections (`*ReadModel`, `*Projection`); an architecture rule fails the build. Return DTOs mapped from the entities instead.
- A module whose reads outgrow its entities needs CQRS chosen for it; escalate with `needs:decision` rather than building a read side by hand.
