# Changelog
## [Unreleased]

### Added

- Source Generator Support
- Enforced jump-table creation

### Changed

- IPacket, IIncomingPacket, IOutgoingPacket interfaces have been changed
  - GetCurrentMaxSize is a required method, needed for slicing the serialized data if necessary
  - Serialization and Deserialization methods now use a Span<byte> instead of byte[]
  - Packet creation both serialization and deserialization require the Span to be created and passed in
- Packet handlers still use a byte[], these must be managed by the user or the corresponding network I/O layer

### Removed

- Removal of IL-Emission-based code generation
