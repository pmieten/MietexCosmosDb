# Security Policy

## Supported versions

The latest released minor version receives security fixes. Pre-release versions are supported on a
best-effort basis.

## Reporting a vulnerability

Do not open a public issue for a security problem. Report it privately through
[GitHub Security Advisories](https://github.com/pmieten/MietexCosmosDb/security/advisories/new) or by
contacting the maintainer listed in the repository profile.

Include the affected version, a minimal reproduction, and the impact you believe is possible. We aim
to acknowledge reports within five business days.

## Known design risks

- **Newtonsoft.Json `TypeNameHandling.Auto`.** The default serializer settings preserve this setting
  for compatibility with existing polymorphic Cosmos documents. Deserializing untrusted JSON with
  this setting can instantiate unexpected types. Do not use the serializer as a general-purpose
  parser, and do not store untrusted serialized payloads with type metadata.
- **Local emulator TLS relaxation.** `CosmosLocalEndpointSupport` accepts any server certificate, but
  only when the connection string points at a loopback address. It never applies to a remote Azure
  endpoint.
- **Distributed lock scope.** `CosmosPartitionLock` is a coordination primitive for a single write
  region with at least Session consistency. It is not an authorization boundary, does not renew
  leases, and does not protect writers that do not follow the same protocol.
- **Connection strings and document bodies.** The library never logs connection strings, account
  keys, or full document bodies. Batch error messages are truncated. Application code remains
  responsible for the data it puts into its own logs and exceptions.
