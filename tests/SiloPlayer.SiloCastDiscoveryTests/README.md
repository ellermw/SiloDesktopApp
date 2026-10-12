# SiloCast discovery regression tests

This project links the production Win32 advertiser/network sources without launching WinUI or loading user settings.

```powershell
dotnet test tests\SiloPlayer.SiloCastDiscoveryTests -nologo
```

Coverage includes multicast/interface selection, native callback ownership, asynchronous failure, cancellation with a late successful callback, withdrawal, restart, and generation fencing. Fake native operations model callbacks; the production lifetime and cleanup code remains real.

For an actual Windows registration/withdrawal smoke on an active physical Private network:

```powershell
$env:SILOCAST_NATIVE_SMOKE = '1'
dotnet test tests\SiloPlayer.SiloCastDiscoveryTests -nologo
```

Optional `SILOCAST_NATIVE_INTERFACE_INDEX` and `SILOCAST_NATIVE_EXCLUDED_INTERFACE_INDEX` assert a known physical adapter is included and a known VPN/virtual adapter is excluded. Use current interface indexes from `Get-NetAdapter` and categories from `Get-NetConnectionProfile`. The smoke publishes synthetic test metadata for the duration of the test; it does not bind a listener or change firewall settings.

Native registration success establishes Windows API functionality. Phone discovery and TLS/control interoperability still require separate device testing.
