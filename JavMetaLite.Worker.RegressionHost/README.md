# Fixed response Worker regression host

This executable is a test dependency, not an alternative application engine. It
references the production `Workspace`, JSON writer/framing, LibreDMM and R18.dev
clients, metadata parsers, request scheduler, and file transaction service.

Only the host implements `fixture.configure` and `fixture.requests`. Its injected
HTTP message handler returns explicit response queues and records every requested
URL. Unregistered requests fail; they cannot reach the network. R18 cooldowns use
an injected clock while body timeouts and cancellation still use real task tokens.
The optional cleanup failure returns an error from the real transaction service's
cleanup delegate after actual staging/commit or rollback. This proves Worker
reporting and original-file preservation under an injected cleanup failure, not
physical unplugging or permission behavior of an external volume.

The production Worker accepts none of these fixture methods or CLI parameters.
The project sets `IsPublishable=false` and `IsPackable=false`; application packaging
publishes only `JavMetaLite.Worker`. No credentials or live-provider access are
needed for this suite.

Use `scripts/macos/test.sh` for the complete gate. To run manually after building
both projects with the pinned SDK:

```sh
export JAVMETALITE_SEARCH_TEST_HOST="$PWD/JavMetaLite.Worker.RegressionHost/bin/Release/net10.0/JavMetaLite.Worker.RegressionHost"
python3 scripts/macos/test_worker.py \
  "$PWD/JavMetaLite.Worker/bin/Release/net10.0/JavMetaLite.Worker" --require-search-host
```

A framework-dependent build also requires `DOTNET_ROOT` to point at that SDK.
`--require-search-host` fails rather than skipping when the host is absent.
`--production-only` intentionally runs only cases exercising the supplied
production Worker, for verifying a self-contained app extracted from its ZIP.
Without either flag, unavailable host tests are visibly reported as skipped.

The derived fixtures use synthetic titles/media and technical case identifiers.
The private source regression attachment is not copied into this project.
