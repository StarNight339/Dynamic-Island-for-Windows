"""
Integration tests for the local HTTP API. Standard library only.

Start the app first (`dotnet run`), then:
    python -m unittest discover -s tests -v

Set DYNAMIC_ISLAND_URL to test another port, e.g. http://localhost:5180
"""

import json
import os
import time
import unittest
import urllib.error
import urllib.request

BASE = os.environ.get("DYNAMIC_ISLAND_URL", "http://localhost:5179").rstrip("/")


def request(method, path, body=None, raw=None):
    """Returns (status, parsed JSON). `raw` sends the string as-is (for malformed bodies)."""
    data = raw.encode() if raw is not None else json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method,
                                 headers={"Content-Type": "application/json; charset=utf-8"})
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            return r.status, json.loads(r.read() or b"null")
    except urllib.error.HTTPError as e:
        with e:
            return e.code, json.loads(e.read() or b"null")


def post(path, body=None, raw=None):
    return request("POST", path, body, raw)


def setUpModule():
    try:
        request("GET", "/")
    except urllib.error.URLError as e:
        raise unittest.SkipTest(f"DynamicIsland is not running at {BASE} ({e.reason})")


class HealthTests(unittest.TestCase):
    def test_health(self):
        self.assertEqual(request("GET", "/"), (200, {"ok": True, "app": "DynamicIsland"}))


class NotifyTests(unittest.TestCase):
    def test_minimal(self):
        self.assertEqual(post("/notify", {"title": "Test"}), (200, {"ok": True}))

    def test_all_fields(self):
        status, _ = post("/notify", {"title": "Build passed 🎉", "body": "42 tests", "icon": "🤖",
                                     "color": "#34C759", "duration": 2})
        self.assertEqual(status, 200)

    def test_duration_as_string(self):
        self.assertEqual(post("/notify", {"title": "x", "duration": "2"})[0], 200)

    def test_bad_image_falls_back_to_icon(self):
        status, _ = post("/notify", {"title": "x", "image": "C:/does/not/exist.png", "duration": 1})
        self.assertEqual(status, 200)


class ProgressTests(unittest.TestCase):
    def tearDown(self):
        for pid in ("t-life", "t-values", "default", "t-a", "t-b"):
            post("/progress", {"id": pid, "dismiss": True})

    def test_lifecycle(self):
        self.assertEqual(post("/progress", {"id": "t-life", "title": "Downloading", "body": "file.bin",
                                            "progress": 0})[0], 200)
        for p in (25, 50, 75):
            self.assertEqual(post("/progress", {"id": "t-life", "progress": p})[0], 200)
            time.sleep(0.2)
        self.assertEqual(post("/progress", {"id": "t-life", "progress": 100}), (200, {"ok": True}))

    def test_values_are_accepted(self):
        # Out-of-range values are clamped; numeric strings parse; junk is ignored.
        for value in (-50, 0, 33.3, 100, 250, "60", "abc"):
            with self.subTest(value=value):
                self.assertEqual(post("/progress", {"id": "t-values", "progress": value})[0], 200)

    def test_default_id(self):
        self.assertEqual(post("/progress", {"progress": 10})[0], 200)
        self.assertEqual(post("/progress", {"dismiss": True})[0], 200)

    def test_dismiss_unknown_id(self):
        self.assertEqual(post("/progress", {"id": "never-created", "dismiss": True})[0], 200)

    def test_multiple_ids(self):
        self.assertEqual(post("/progress", {"id": "t-a", "title": "A", "progress": 20})[0], 200)
        self.assertEqual(post("/progress", {"id": "t-b", "title": "B", "progress": 80})[0], 200)

    def test_empty_body(self):
        # An empty body is treated as {} -> id "default", progress 0.
        self.assertEqual(post("/progress", raw="")[0], 200)


class TimerTests(unittest.TestCase):
    def test_start_and_cancel(self):
        self.assertEqual(post("/timer", {"seconds": 60})[0], 200)
        self.assertEqual(post("/timer", {"seconds": 0})[0], 200)


class ClaudeTests(unittest.TestCase):
    def test_short_form_events(self):
        for event in ("working", "waiting", "done", "idle"):
            with self.subTest(event=event):
                self.assertEqual(post("/claude", {"event": event, "project": "unittest"})[0], 200)


class ErrorTests(unittest.TestCase):
    def test_invalid_json(self):
        self.assertEqual(post("/progress", raw="{bad"), (400, {"error": "invalid JSON"}))

    def test_unknown_endpoint(self):
        self.assertEqual(post("/nope", {}), (404, {"error": "unknown endpoint"}))

    def test_wrong_method(self):
        self.assertEqual(request("GET", "/progress"), (405, {"error": "use POST"}))


if __name__ == "__main__":
    unittest.main()
