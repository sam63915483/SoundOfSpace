import unittest

from audioscan import VERSION


class SmokeTest(unittest.TestCase):
    def test_package_imports_and_has_a_version(self):
        self.assertEqual(VERSION, 1)


if __name__ == "__main__":
    unittest.main()
