#!/usr/bin/env python3
"""Minimal RimConnect backend for local Zombieland compatibility testing."""

from __future__ import annotations

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any
from urllib.parse import parse_qs, urlparse


class State:
    def __init__(self, token: str, silver_award_points: int) -> None:
        self.token = token
        self.silver_award_points = silver_award_points
        self.valid_commands: list[dict[str, Any]] = []
        self.updated_options: list[dict[str, Any]] = []
        self.world_names: list[str] = []
        self.deleted_command_ids: list[str] = []


def read_json(handler: BaseHTTPRequestHandler) -> dict[str, Any]:
    length = int(handler.headers.get("Content-Length", "0") or "0")
    if length <= 0:
        return {}
    raw = handler.rfile.read(length)
    try:
        data = json.loads(raw.decode("utf-8"))
    except json.JSONDecodeError:
        return {}
    return data if isinstance(data, dict) else {}


def command_to_option(command: dict[str, Any]) -> dict[str, Any]:
    return {
        "actionHash": command.get("actionHash", ""),
        "localCooldownMs": int(command.get("localCooldownMs") or 0),
        "globalCooldownMs": int(command.get("globalCooldownMs") or 0),
        "costSilverStore": int(command.get("costSilverStore") or 0),
    }


class Handler(BaseHTTPRequestHandler):
    server_version = "RimConnectLocalBackend/1.0"
    state: State

    def log_message(self, format: str, *args: Any) -> None:
        print("%s - %s" % (self.log_date_time_string(), format % args), flush=True)

    def send_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        data = read_json(self)

        if parsed.path == "/auth/mod":
            self.send_json(200, {"token": self.state.token})
            return

        if parsed.path == "/command/valid":
            commands = data.get("validCommands") or []
            if isinstance(commands, list):
                self.state.valid_commands.extend(
                    command for command in commands if isinstance(command, dict)
                )
            self.send_json(200, {"success": True})
            return

        if parsed.path == "/command/options":
            options = data.get("commandOptions") or []
            if isinstance(options, list):
                self.state.updated_options.extend(
                    option for option in options if isinstance(option, dict)
                )
            self.send_json(200, {"success": True})
            return

        if parsed.path == "/loyalty/config":
            value = data.get("silverAwardPoints")
            if isinstance(value, int):
                self.state.silver_award_points = value
            self.send_json(200, {"success": True})
            return

        if parsed.path == "/mod/world":
            world = data.get("world")
            if isinstance(world, str):
                self.state.world_names.append(world)
            self.send_json(200, {"success": True})
            return

        self.send_json(404, {"error": "unknown endpoint", "path": parsed.path})

    def do_GET(self) -> None:
        parsed = urlparse(self.path)

        if parsed.path == "/command/valid":
            self.send_json(
                200,
                {"commands": [command_to_option(command) for command in self.state.valid_commands]},
            )
            return

        if parsed.path == "/command/list":
            self.send_json(200, {"commands": []})
            return

        if parsed.path == "/loyalty/config":
            self.send_json(200, {"silverAwardPoints": self.state.silver_award_points})
            return

        if parsed.path == "/health":
            self.send_json(
                200,
                {
                    "ok": True,
                    "validCommandCount": len(self.state.valid_commands),
                    "updatedOptionCount": len(self.state.updated_options),
                    "deletedCommandCount": len(self.state.deleted_command_ids),
                    "worldNames": self.state.world_names,
                },
            )
            return

        self.send_json(404, {"error": "unknown endpoint", "path": parsed.path})

    def do_DELETE(self) -> None:
        parsed = urlparse(self.path)

        if parsed.path == "/command/list":
            self.state.deleted_command_ids.append(
                parse_qs(parsed.query).get("toDelete", ["0"])[0]
            )
            self.send_json(200, {"success": True})
            return

        self.send_json(404, {"error": "unknown endpoint", "path": parsed.path})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--token", default="zl-local-rimconnect-token")
    parser.add_argument("--silver-award-points", type=int, default=0)
    args = parser.parse_args()

    state = State(args.token, args.silver_award_points)
    handler_type = type("ConfiguredHandler", (Handler,), {"state": state})
    server = ThreadingHTTPServer((args.host, args.port), handler_type)
    print(f"RimConnect local backend listening on http://{args.host}:{args.port}/", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("Stopping RimConnect local backend.", flush=True)
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
