#!/usr/bin/env python3
"""
CityLife / Starfall Safe Local Asset Catalog Server
Strictly binds to 127.0.0.1. Read-only (GET/HEAD only, no directory listings).
Rigorous traversal & boundary protections:
- Validates Host header strictly (127.0.0.1 or localhost only)
- Immediately rejects raw backslashes and encoded traversal attempts
- Blocks symlinks and reparse points
- Restricts served paths to approved catalog assets and explicit milestone images
- Strictly denies Library, Builds, private logs, saves, keys, credentials
"""

import argparse
import ctypes
import http.server
import os
from pathlib import Path, PurePosixPath
import socketserver
import sys
import urllib.parse

ALLOWED_DIRECTORIES = (
    "tools/asset-catalog",
    "Assets/CityLife",
    "evidence/milestones"
)

ALLOWED_FILE_EXTENSIONS = {
    ".html", ".js", ".mjs", ".css", ".json",
    ".png", ".jpg", ".jpeg", ".obj", ".fbx",
    ".cs", ".shader", ".hlsl", ".txt", ".md"
}

FORBIDDEN_NAME_PATTERNS = {
    ".git", ".github", "library", "builds", "usersettings", "sessions", "worlds",
    "backupthisfolder_butdontshipitwithyourgame", "private", "outbox", "outboxes"
}

FORBIDDEN_SUFFIXES = {
    ".key", ".pem", ".p12", ".pfx", ".jks", ".keystore",
    ".sqlite", ".sqlite3", ".db", ".log", ".env", ".save", ".dat"
}

MIME_TYPES = {
    ".html": "text/html; charset=utf-8",
    ".htm": "text/html; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".mjs": "application/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".obj": "text/plain; charset=utf-8",
    ".fbx": "application/octet-stream",
    ".cs": "text/plain; charset=utf-8",
    ".shader": "text/plain; charset=utf-8",
    ".hlsl": "text/plain; charset=utf-8",
    ".txt": "text/plain; charset=utf-8",
    ".md": "text/markdown; charset=utf-8",
    ".unity": "application/octet-stream"
}

def is_reparse_point(path: Path) -> bool:
    """Check if file or directory is a Windows reparse point (junction/symlink)."""
    if path.is_symlink():
        return True
    if os.name == 'nt':
        try:
            attrs = ctypes.windll.kernel32.GetFileAttributesW(str(path))
            if attrs != -1 and (attrs & 0x400):  # FILE_ATTRIBUTE_REPARSE_POINT = 0x400
                return True
        except Exception:
            pass
    return False

class SafeCatalogHandler(http.server.BaseHTTPRequestHandler):
    repo_root = Path(__file__).resolve().parent.parent.parent

    def do_HEAD(self):
        self.handle_request(send_body=False)

    def do_GET(self):
        self.handle_request(send_body=True)

    def do_POST(self):
        self.send_error(405, "Method Not Allowed: Read-only server")

    def do_PUT(self):
        self.send_error(405, "Method Not Allowed: Read-only server")

    def do_DELETE(self):
        self.send_error(405, "Method Not Allowed: Read-only server")

    def handle_request(self, send_body=True):
        # 1. Host header validation: strictly local loopback
        host = self.headers.get("Host", "").split(":")[0].strip().lower()
        if host not in ("127.0.0.1", "localhost", ""):
            self.send_error(400, "Bad Request: Unexpected Host header (loopback only)")
            return

        raw_request_uri = self.path

        # 2. Reject backslashes and encoded traversal in raw URI before decoding
        if "\\" in raw_request_uri or "%5c" in raw_request_uri.lower() or "\0" in raw_request_uri:
            self.send_error(403, "Forbidden: Backslash and null traversal tokens strictly denied")
            return

        parsed = urllib.parse.urlsplit(raw_request_uri)
        raw_path = urllib.parse.unquote(parsed.path)

        # Redirect root to catalog frontend
        if raw_path in ("/", ""):
            self.send_response(302)
            self.send_header("Location", "/tools/asset-catalog/index.html")
            self.end_headers()
            return

        # Double check for backslashes and nulls in decoded path
        if "\\" in raw_path or "\0" in raw_path:
            self.send_error(403, "Forbidden: Invalid character in path")
            return

        clean_path = raw_path.lstrip("/")
        posix = PurePosixPath(clean_path)

        # 3. Check traversal parts
        if ".." in posix.parts or any(":" in part for part in posix.parts):
            self.send_error(403, "Forbidden: Traversal detected")
            return

        # 4. Check forbidden directory and file names
        for part in posix.parts:
            lower = part.lower()
            if lower in FORBIDDEN_NAME_PATTERNS or lower.startswith(".env"):
                self.send_error(403, "Forbidden: Restricted directory path")
                return

        ext = posix.suffix.lower()
        if ext in FORBIDDEN_SUFFIXES or ext not in ALLOWED_FILE_EXTENSIONS:
            self.send_error(403, "Forbidden: Unapproved file type")
            return

        # 5. Whitelist allowed prefixes
        normalized_str = clean_path.replace("\\", "/")
        if not any(normalized_str.startswith(prefix + "/") for prefix in ALLOWED_DIRECTORIES):
            self.send_error(403, "Forbidden: Path outside approved catalog assets")
            return

        # 6. Resolve absolute path and verify it stays inside repo_root
        target_path = (self.repo_root / posix).resolve()
        try:
            target_path.relative_to(self.repo_root)
        except ValueError:
            self.send_error(403, "Forbidden: Path escaped workspace root")
            return

        # 7. Deny symlinks and reparse points
        if is_reparse_point(target_path):
            self.send_error(403, "Forbidden: Symlinks and reparse points are denied")
            return

        # Check each parent component for reparse points up to repo root
        curr = target_path.parent
        while curr != self.repo_root and curr != curr.parent:
            if is_reparse_point(curr):
                self.send_error(403, "Forbidden: Parent reparse point denied")
                return
            curr = curr.parent

        # 8. Deny directory listings
        if target_path.is_dir():
            self.send_error(403, "Forbidden: Directory listing disabled")
            return

        if not target_path.is_file():
            self.send_error(404, "Not Found")
            return

        # 9. Serve file with safety headers
        try:
            file_bytes = target_path.read_bytes()
            mime_type = MIME_TYPES.get(ext, "application/octet-stream")
            self.send_response(200)
            self.send_header("Content-Type", mime_type)
            self.send_header("Content-Length", str(len(file_bytes)))
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("X-Frame-Options", "DENY")
            self.send_header("Content-Security-Policy", "default-src 'self' 'unsafe-inline'; img-src 'self' data:; media-src 'none'")
            self.send_header("Cache-Control", "no-cache, must-revalidate")
            self.end_headers()
            if send_body:
                self.wfile.write(file_bytes)
        except Exception as e:
            self.send_error(500, f"Internal Server Error: {e}")

    def log_message(self, format, *args):
        sys.stderr.write(f"[127.0.0.1] {args[0]} - {args[1]}\n")

def run(port=8080):
    host = "127.0.0.1"
    server_address = (host, port)
    socketserver.TCPServer.allow_reuse_address = True
    try:
        with socketserver.TCPServer(server_address, SafeCatalogHandler) as httpd:
            print("================================================================")
            print(" Starfall / CityLife Safe Asset Catalog Server (Read-Only)      ")
            print(f" Bound to loopback: http://{host}:{port}/                     ")
            print(f" Catalog Interface: http://{host}:{port}/tools/asset-catalog/index.html")
            print("================================================================")
            httpd.serve_forever()
    except OSError as e:
        if "address already in use" in str(e).lower():
            run(port + 1)
        else:
            raise

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Serve CityLife asset catalog locally on 127.0.0.1")
    parser.add_argument("--port", type=int, default=8080, help="Port to bind on (default: 8080)")
    args = parser.parse_args()
    run(args.port)
