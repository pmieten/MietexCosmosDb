## Summary

## Test plan

- [ ] `dotnet test -f net8.0 --filter Category=Unit`
- [ ] `dotnet test -f net10.0 --filter Category=Unit`
- [ ] `dotnet test -f net10.0 --filter Category=Integration` (real emulator)
- [ ] `dotnet test -f net10.0 --filter Category=Packaging`

## Risk

- [ ] API change
- [ ] Storage shape change
- [ ] Migration guide update needed
- [ ] Security-sensitive change

## Checklist

- [ ] No Cosmos SDK mocks were introduced.
- [ ] Connection strings and account keys are not logged.
- [ ] New behavior has a test that fails without the change.
