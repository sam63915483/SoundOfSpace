"""Read-only scanners that turn a Unity project into an audio manifest.

No module in this package imports Unity, opens a network socket, or writes
anywhere under Assets/ except through `manifest.write_manifest`.
"""

VERSION = 1
