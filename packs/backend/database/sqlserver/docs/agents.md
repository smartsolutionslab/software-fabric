## Database: SQL Server

- Local SQL Server comes from Aspire (`dotnet run --project src/AppHost`); never install or point at one by hand.
- Names in the database are snake_case; schemas are per module.
- Do not turn on `EnableRetryOnFailure`: units of work open their own transactions, which a retrying execution strategy refuses.
