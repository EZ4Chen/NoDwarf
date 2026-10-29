"""Regression fixtures extracted from the failing and Apple-re-signed alpha.3 app."""
import unittest

from macos_sign import validate_der

# The original passes codesign --verify but is killed by macOS 26 AMFI.
GODOT_ENTITLEMENTS = bytes.fromhex("318200ef308200280c82001f636f6d2e6170706c652e73656375726974792e63732e616c6c6f772d6a697401820001013082003f0c820036636f6d2e6170706c652e73656375726974792e63732e616c6c6f772d756e7369676e65642d65786563757461626c652d6d656d6f727901820001013082003f0c820036636f6d2e6170706c652e73656375726974792e63732e616c6c6f772d64796c642d656e7669726f6e6d656e742d7661726961626c65730182000101308200390c820030636f6d2e6170706c652e73656375726974792e63732e64697361626c652d6c6962726172792d76616c69646174696f6e0182000101")
APPLE_ENTITLEMENTS = bytes.fromhex("7081dd020101b081d7303b0c36636f6d2e6170706c652e73656375726974792e63732e616c6c6f772d64796c642d656e7669726f6e6d656e742d7661726961626c65730101ff30240c1f636f6d2e6170706c652e73656375726974792e63732e616c6c6f772d6a69740101ff303b0c36636f6d2e6170706c652e73656375726974792e63732e616c6c6f772d756e7369676e65642d65786563757461626c652d6d656d6f72790101ff30350c30636f6d2e6170706c652e73656375726974792e63732e64697361626c652d6c6962726172792d76616c69646174696f6e0101ff")


class DerEntitlementTests(unittest.TestCase):
    def test_rejects_published_godot_signature(self):
        with self.assertRaises(ValueError):
            validate_der(GODOT_ENTITLEMENTS)

    def test_accepts_apple_signature(self):
        validate_der(APPLE_ENTITLEMENTS)

    def test_rejects_truncated_signature(self):
        for end in range(len(APPLE_ENTITLEMENTS)):
            with self.subTest(end=end), self.assertRaises(ValueError):
                validate_der(APPLE_ENTITLEMENTS[:end])

    def test_rejects_nested_nonminimal_length(self):
        with self.assertRaises(ValueError):
            validate_der(bytes.fromhex("3004018101ff"))

    def test_rejects_trailing_data(self):
        with self.assertRaises(ValueError):
            validate_der(APPLE_ENTITLEMENTS + b"\x00")


if __name__ == "__main__":
    unittest.main()
