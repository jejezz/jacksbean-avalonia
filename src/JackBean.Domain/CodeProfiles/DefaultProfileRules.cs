namespace JackBean.Domain.CodeProfiles;

/// <summary>
/// 내장 기본 규칙(JSON). Flutter 판 `assets/rules.yaml` 을 그대로 옮긴 것이다 — 사용자 규칙 파일이 같은 `id` 의 유형을 덮어쓰고 새 `id` 는 더한다.
/// 코드 상수로 두는 것은 리소스 파일 없이도 어느 배포 형태(NativeAOT 단일 파일)에서나 읽히게 하려는 것이다.
/// </summary>
public static class DefaultProfileRules
{
    public const string Json = """
{
  "version": 1,
  "scan": {
    "maxDepth": 3,
    "maxFiles": 20000,
    "skipDirs": [
      ".git",
      ".repo",
      "out",
      "build",
      "node_modules",
      "prebuilts",
      ".venv",
      "venv",
      "__pycache__",
      ".gradle",
      ".idea",
      "managed_components",
      ".cache",
      "dist",
      "target"
    ]
  },
  "variables": {
    "aospRoot": {
      "findUpward": {
        "anyOf": [
          ".repo",
          "build/envsetup.sh"
        ],
        "maxLevels": 12
      }
    }
  },
  "types": [
    {
      "id": "esp-idf",
      "label": "ESP-IDF",
      "priority": 100,
      "match": {
        "any": [
          {
            "file": "sdkconfig"
          },
          {
            "file": "**/idf_component.yml"
          },
          {
            "fileContains": {
              "glob": "CMakeLists.txt",
              "pattern": "IDF_PATH|project\\.cmake"
            }
          }
        ]
      },
      "suppresses": [
        "cmake"
      ],
      "profile": "ESP-IDF",
      "extensions": [
        "espressif.esp-idf-extension",
        "llvm-vs-code-extensions.vscode-clangd"
      ],
      "settings": {
        "files.watcherExclude": {
          "**/build/**": true,
          "**/managed_components/**": true
        },
        "search.exclude": {
          "**/build": true,
          "**/managed_components": true
        }
      },
      "notes": [
        "idf.port 등 보드별 설정은 프로젝트 settings.json 에 직접 추가하세요."
      ]
    },
    {
      "id": "aosp-module",
      "label": "AOSP 모듈",
      "priority": 90,
      "match": {
        "all": [
          {
            "any": [
              {
                "file": "Android.bp"
              },
              {
                "file": "Android.mk"
              }
            ]
          },
          {
            "variable": "aospRoot"
          }
        ]
      },
      "suppresses": [
        "cmake",
        "make"
      ],
      "profile": "C++ AOSP",
      "extensions": [
        "llvm-vs-code-extensions.vscode-clangd"
      ],
      "settings": {
        "clangd.arguments": [
          "--compile-commands-dir=${aospRoot}/out/soong/development/ide/compdb",
          "--background-index",
          "--pch-storage=disk",
          "--malloc-trim",
          "-j=4"
        ],
        "files.watcherExclude": {
          "**/out/**": true,
          "**/.cache/**": true
        },
        "search.followSymlinks": false
      },
      "notes": [
        "compdb 생성: cd ${aospRoot} && source build/envsetup.sh && lunch <target> && SOONG_GEN_COMPDB=1 SOONG_GEN_COMPDB_DEBUG=1 m nothing"
      ]
    },
    {
      "id": "java-gradle",
      "label": "Java (Gradle)",
      "priority": 70,
      "match": {
        "any": [
          {
            "file": "build.gradle"
          },
          {
            "file": "build.gradle.kts"
          },
          {
            "file": "settings.gradle"
          },
          {
            "file": "settings.gradle.kts"
          }
        ]
      },
      "profile": "Java",
      "extensions": [
        "vscjava.vscode-java-pack",
        "vscjava.vscode-gradle"
      ],
      "settings": {
        "java.import.exclusions": [
          "**/build/**",
          "**/.gradle/**",
          "**/node_modules/**"
        ],
        "java.configuration.updateBuildConfiguration": "interactive"
      }
    },
    {
      "id": "java-maven",
      "label": "Java (Maven)",
      "priority": 70,
      "match": {
        "any": [
          {
            "file": "pom.xml"
          }
        ]
      },
      "profile": "Java",
      "extensions": [
        "vscjava.vscode-java-pack"
      ],
      "settings": {
        "java.import.exclusions": [
          "**/target/**",
          "**/node_modules/**"
        ],
        "java.configuration.updateBuildConfiguration": "interactive"
      }
    },
    {
      "id": "cmake",
      "label": "CMake",
      "priority": 50,
      "match": {
        "any": [
          {
            "file": "CMakeLists.txt"
          }
        ]
      },
      "profile": "C++",
      "extensions": [
        "llvm-vs-code-extensions.vscode-clangd",
        "ms-vscode.cmake-tools"
      ],
      "settings": {
        "cmake.configureOnOpen": false,
        "cmake.configureOnEdit": false,
        "cmake.buildDirectory": "${workspaceFolder}/build",
        "cmake.configureSettings": {
          "CMAKE_EXPORT_COMPILE_COMMANDS": "ON"
        }
      }
    },
    {
      "id": "linux-kernel",
      "label": "Linux 커널 / 부트로더",
      "priority": 60,
      "match": {
        "all": [
          {
            "file": "Kconfig"
          },
          {
            "file": "Makefile"
          }
        ]
      },
      "suppresses": [
        "make"
      ],
      "profile": "Embedded Make",
      "extensions": [
        "llvm-vs-code-extensions.vscode-clangd"
      ],
      "settings": {
        "files.watcherExclude": {
          "**/*.o": true,
          "**/*.cmd": true
        }
      },
      "notes": [
        "빌드 후 ./scripts/clang-tools/gen_compile_commands.py 로 compile_commands.json 을 생성하세요."
      ]
    },
    {
      "id": "make",
      "label": "Makefile",
      "priority": 40,
      "match": {
        "any": [
          {
            "file": "Makefile"
          },
          {
            "file": "makefile"
          },
          {
            "file": "GNUmakefile"
          }
        ]
      },
      "profile": "Embedded Make",
      "extensions": [
        "llvm-vs-code-extensions.vscode-clangd"
      ],
      "settings": {
        "[makefile]": {
          "editor.insertSpaces": false
        }
      },
      "notes": [
        "bear -- make 로 compile_commands.json 을 생성하면 clangd 가 정확해집니다."
      ]
    },
    {
      "id": "node",
      "label": "JavaScript / TypeScript",
      "priority": 60,
      "match": {
        "any": [
          {
            "file": "package.json"
          },
          {
            "file": "tsconfig.json"
          },
          {
            "file": "jsconfig.json"
          }
        ]
      },
      "profile": "Web / Python",
      "extensions": [
        "dbaeumer.vscode-eslint",
        "esbenp.prettier-vscode"
      ],
      "settings": {
        "eslint.run": "onSave",
        "files.watcherExclude": {
          "**/node_modules/**": true,
          "**/dist/**": true
        },
        "search.exclude": {
          "**/node_modules": true,
          "**/dist": true
        }
      }
    },
    {
      "id": "python",
      "label": "Python",
      "priority": 60,
      "match": {
        "any": [
          {
            "file": "pyproject.toml"
          },
          {
            "file": "requirements.txt"
          },
          {
            "file": "setup.py"
          }
        ]
      },
      "profile": "Web / Python",
      "extensions": [
        "ms-python.python"
      ],
      "settings": {
        "python.analysis.diagnosticMode": "openFilesOnly",
        "python.analysis.exclude": [
          "**/.venv",
          "**/__pycache__"
        ],
        "files.watcherExclude": {
          "**/.venv/**": true,
          "**/__pycache__/**": true
        }
      }
    }
  ],
  "sizeRules": [
    {
      "when": {
        "truncated": true
      },
      "settings": {
        "search.followSymlinks": false
      },
      "notes": [
        "트리가 매우 큽니다. 전체 대신 작업 모듈 폴더만 여는 것을 권장합니다."
      ]
    }
  ]
}
""";
}
