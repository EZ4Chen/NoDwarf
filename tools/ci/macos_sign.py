"""Use Apple's signer and reject malformed DER before shipping a macOS app."""
from pathlib import Path
import plistlib
import subprocess


def validate_der(data):
    """Reject truncated or non-minimal DER lengths, including Godot #124317."""
    def element(offset, end):
        if offset + 2 > end:
            raise ValueError("Truncated DER header")
        tag, length = data[offset:offset + 2]
        offset += 2
        if tag & 0x1f == 0x1f:
            raise ValueError("Unsupported DER high tag")
        if length & 0x80:
            count = length & 0x7f
            if not count or offset + count > end or data[offset] == 0:
                raise ValueError("Invalid or non-minimal DER length")
            length = int.from_bytes(data[offset:offset + count], "big")
            if length < 128:
                raise ValueError("Non-minimal DER long length")
            offset += count
        limit = offset + length
        if limit > end:
            raise ValueError("Truncated DER value")
        if tag & 0x20:
            while offset < limit:
                offset = element(offset, limit)
        return limit

    if not data or element(0, len(data)) != len(data):
        raise ValueError("Missing or trailing DER data")


def entitlements(app, format):
    return subprocess.run(
        ["codesign", "--display", "--entitlements", "-", f"--{format}", str(app)],
        check=True, capture_output=True, timeout=30,
    ).stdout


def verify_app(app):
    subprocess.run(["codesign", "--verify", "--deep", "--strict", str(app)],
                   check=True, timeout=60)
    # codesign --verify accepts the built-in signer's BER-style lengths, but
    # macOS 26 AMFI rejects them before the process can start.
    validate_der(entitlements(app, "der"))


def sign_app(app, workspace):
    app = Path(app)
    permissions = plistlib.loads(entitlements(app, "xml"))
    if not isinstance(permissions, dict) or not permissions:
        raise ValueError("Missing exported .NET entitlements")
    path = Path(workspace) / "macos-entitlements.plist"
    path.write_bytes(plistlib.dumps(permissions))
    subprocess.run([
        "codesign", "--force", "--sign", "-", "--options", "runtime",
        "--generate-entitlement-der", "--entitlements", str(path), str(app),
    ], check=True, timeout=60)
    if plistlib.loads(entitlements(app, "xml")) != permissions:
        raise ValueError("Signing changed exported entitlements")
    verify_app(app)
