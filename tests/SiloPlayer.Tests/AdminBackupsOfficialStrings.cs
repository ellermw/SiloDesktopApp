// The official English for the Backups strings that changed or were added since 92ee051878c2a791940ec9598e2bdd0f04890b3e.
// Source: Silo-Server web/src/locales/en.json at 4a1ee058eb3cfc74941f93a4aa57db6b617ca5f0 (read with
// `git show 4a1ee058eb3cfc74941f93a4aa57db6b617ca5f0:web/src/locales/en.json`). Each key's value below is that file's own text for the key, copied
// line for line (the compiled message ASTs the official client ships), so it is independent of the desktop catalog it checks. en.json is identical
// at 8d253adbf (the commit the catalog was generated from) and 4a1ee058e.
// restore.choose.hideThem and showThem are not here: 5641447d6 moved them (with five other messages) to a select on the exact amount, and
// AdminBackupsEnvironmentAndAmountTests checks them against the official text at 93794454b.
namespace SiloPlayer.Tests;

internal static class AdminBackupsOfficialStrings
{
    internal const string EnJsonCommit = "4a1ee058eb3cfc74941f93a4aa57db6b617ca5f0";

    internal const string AstJson = """
{
  "web.admin.backups.restore.blocked.collectionNotMapped": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — the collection it shows isn't here or in the backup"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — the collection they show isn't here or in the backup"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.configNeedsAnotherKey": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its settings are encrypted with a different SECRET_KEY"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their settings are encrypted with a different SECRET_KEY"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.fallback": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — this server can't take it"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — this server can't take them"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.filesMissingHere": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its files are missing on this server"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their files are missing on this server"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.folderMissing": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its folders aren't on this server"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their folders aren't on this server"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.groupNotOnThisServer": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its access group isn't on this server"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their access group isn't on this server"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.itemNotOnThisServer": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its title isn't in this server's catalog"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their title isn't in this server's catalog"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.keptStricter": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — it would widen access, so this server keeps its own stricter values"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — they would widen access, so this server keeps its stricter values"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.libraryNotMapped": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its library isn't on this server"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their library isn't on this server"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.libraryNotRestorable": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its library can't be restored here"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their library can't be restored here"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.managedByEnvironment": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — this server's environment sets the settings that differ, or part of their connection"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — this server's environment sets the settings that differ, or part of their connection"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.needsAnotherKey": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — it's encrypted with a different SECRET_KEY"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — they're encrypted with a different SECRET_KEY"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.needsInstall": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — it needs to be installed from a plugin catalog first"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — they need to be installed from a plugin catalog first"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.needsPrimaryProfile": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — its primary profile has to be restored with it"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — their primary profile has to be restored with them"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.notInBackup": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — the backup doesn't have it"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — the backup doesn't have them"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.policyWider": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — this server would give it more access than the backup did"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — this server would give them more access than the backup did"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.same": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — it's already the same here"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — they're already the same here"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.unreadable": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — the backup's copy of it can't be read"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — the backup's copy of them can't be read"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.blocked.unverifiedBackup": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — it connects to outside services and this server can't verify the backup"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " can't be restored — they connect to outside services and this server can't verify the backup"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.caveat.environmentSettings": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " setting keeps its current value because this server's environment sets it, or part of its connection."
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " settings keep their current values because this server's environment sets them, or part of their connection."
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.reason.managedByEnvironment": [
    {
      "type": 0,
      "value": "This server's environment sets the settings that differ, or part of their connection, so this server keeps its current values."
    }
  ],
  "web.admin.backups.restore.rows.change.backdrop": [
    {
      "type": 0,
      "value": "Backdrop"
    }
  ],
  "web.admin.backups.restore.rows.change.collectionItems": [
    {
      "type": 6,
      "value": "count",
      "options": {
        "one": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " collection item"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 1,
              "value": "count_text"
            },
            {
              "type": 0,
              "value": " collection items"
            }
          ]
        }
      },
      "offset": 0,
      "pluralType": "cardinal"
    }
  ],
  "web.admin.backups.restore.rows.change.collectionReplaced": [
    {
      "type": 0,
      "value": "Collection replaced"
    }
  ],
  "web.admin.backups.restore.rows.change.configuration": [
    {
      "type": 0,
      "value": "Configuration"
    }
  ],
  "web.admin.backups.restore.rows.change.description": [
    {
      "type": 0,
      "value": "Description"
    }
  ],
  "web.admin.backups.restore.rows.change.enabled": [
    {
      "type": 0,
      "value": "Enabled"
    }
  ],
  "web.admin.backups.restore.rows.change.featured": [
    {
      "type": 0,
      "value": "Featured"
    }
  ],
  "web.admin.backups.restore.rows.change.itemLimit": [
    {
      "type": 0,
      "value": "Item limit"
    }
  ],
  "web.admin.backups.restore.rows.change.itemSort": [
    {
      "type": 0,
      "value": "Item sort"
    }
  ],
  "web.admin.backups.restore.rows.change.libraries": [
    {
      "type": 0,
      "value": "Libraries"
    }
  ],
  "web.admin.backups.restore.rows.change.position": [
    {
      "type": 0,
      "value": "Position"
    }
  ],
  "web.admin.backups.restore.rows.change.poster": [
    {
      "type": 0,
      "value": "Poster"
    }
  ],
  "web.admin.backups.restore.rows.change.sortOrder": [
    {
      "type": 0,
      "value": "Sort order"
    }
  ],
  "web.admin.backups.restore.rows.change.source": [
    {
      "type": 0,
      "value": "Source"
    }
  ],
  "web.admin.backups.restore.rows.change.syncSchedule": [
    {
      "type": 0,
      "value": "Sync schedule"
    }
  ],
  "web.admin.backups.restore.rows.change.title": [
    {
      "type": 0,
      "value": "Title"
    }
  ],
  "web.admin.backups.restore.rows.change.titleTranslation": [
    {
      "type": 0,
      "value": "Title translation"
    }
  ],
  "web.admin.backups.restore.rows.change.type": [
    {
      "type": 0,
      "value": "Type"
    }
  ],
  "web.admin.backups.restore.rows.change.visibility": [
    {
      "type": 0,
      "value": "Visibility"
    }
  ],
  "web.admin.backups.restore.rows.change.webOnly": [
    {
      "type": 0,
      "value": "Web only"
    }
  ],
  "web.admin.backups.restore.setting.managedByEnvironment": [
    {
      "type": 1,
      "value": "value"
    },
    {
      "type": 0,
      "value": " (kept: environment)"
    }
  ],
  "web.admin.backups.schedule.timeDescriptionLocal": [
    {
      "type": 0,
      "value": "In the server's time zone. That's "
    },
    {
      "type": 1,
      "value": "time"
    },
    {
      "type": 0,
      "value": " where you are."
    }
  ],
  "web.admin.backups.warnings.hideAccounts": [
    {
      "type": 5,
      "value": "amount",
      "options": {
        "exactlyOne": {
          "value": [
            {
              "type": 0,
              "value": "Hide the account"
            }
          ]
        },
        "exactlyTwo": {
          "value": [
            {
              "type": 0,
              "value": "Hide the accounts"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 0,
              "value": "Hide the accounts"
            }
          ]
        }
      }
    }
  ],
  "web.admin.backups.warnings.hideFiles": [
    {
      "type": 5,
      "value": "amount",
      "options": {
        "exactlyOne": {
          "value": [
            {
              "type": 0,
              "value": "Hide the file"
            }
          ]
        },
        "exactlyTwo": {
          "value": [
            {
              "type": 0,
              "value": "Hide the files"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 0,
              "value": "Hide the files"
            }
          ]
        }
      }
    }
  ],
  "web.admin.backups.warnings.hideGroupFiles": [
    {
      "type": 5,
      "value": "amount",
      "options": {
        "exactlyOne": {
          "value": [
            {
              "type": 0,
              "value": "Hide file"
            }
          ]
        },
        "exactlyTwo": {
          "value": [
            {
              "type": 0,
              "value": "Hide files"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 0,
              "value": "Hide files"
            }
          ]
        }
      }
    }
  ],
  "web.admin.backups.warnings.showAccounts": [
    {
      "type": 5,
      "value": "amount",
      "options": {
        "exactlyOne": {
          "value": [
            {
              "type": 0,
              "value": "Show the account"
            }
          ]
        },
        "exactlyTwo": {
          "value": [
            {
              "type": 0,
              "value": "Show the accounts"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 0,
              "value": "Show the accounts"
            }
          ]
        }
      }
    }
  ],
  "web.admin.backups.warnings.showFiles": [
    {
      "type": 5,
      "value": "amount",
      "options": {
        "exactlyOne": {
          "value": [
            {
              "type": 0,
              "value": "Show the file"
            }
          ]
        },
        "exactlyTwo": {
          "value": [
            {
              "type": 0,
              "value": "Show the files"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 0,
              "value": "Show the files"
            }
          ]
        }
      }
    }
  ],
  "web.admin.backups.warnings.showGroupFiles": [
    {
      "type": 5,
      "value": "amount",
      "options": {
        "exactlyOne": {
          "value": [
            {
              "type": 0,
              "value": "Show file"
            }
          ]
        },
        "exactlyTwo": {
          "value": [
            {
              "type": 0,
              "value": "Show files"
            }
          ]
        },
        "other": {
          "value": [
            {
              "type": 0,
              "value": "Show files"
            }
          ]
        }
      }
    }
  ]
}
""";
}
