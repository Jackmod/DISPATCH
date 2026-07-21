# Real mod config fixtures

These are the **default config `.ini` files** shipped inside the mods DISPATCH
installs, used as test fixtures so the config engine is verified against the exact
key names, section headers and value formats the mods really use — not a synthetic
stand-in that could drift from reality.

They exist because the config catalogue is only correct if its keys match these
files byte-for-byte: the writer only ever changes a key that already exists, so a
guessed key name silently writes nothing. `RealConfigApplyTests` applies the
catalogue to copies of these and asserts the real keys moved.

Each file is an unmodified default configuration template from its mod's free
release; they are included solely as automated-test fixtures. The mods themselves
are never redistributed by DISPATCH.
