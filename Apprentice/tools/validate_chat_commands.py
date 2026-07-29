#!/usr/bin/env python3
"""Validate every Apprentice client and server chat-command registration."""

from validate_world_zones import validate_chat_command_contracts


def main() -> None:
    (
        opened_subcommands,
        closed_subcommands,
        command_trees,
        command_handlers,
    ) = validate_chat_command_contracts()
    print(
        "Chat-command validation passed: "
        f"{opened_subcommands}/{closed_subcommands} balanced subcommands, "
        f"{command_handlers} handlers across "
        f"{command_trees} registration trees; every root, branch and leaf "
        "declares its own privilege."
    )


if __name__ == "__main__":
    main()
