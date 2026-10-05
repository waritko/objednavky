import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildSteps.script
import jetbrains.buildServer.configs.kotlin.triggers.vcs

// Target server: TeamCity 2026.1 (build 222521).
version = "2026.1"

project {
    buildType(BackendCi)
}

object BackendCi : BuildType({
    id("BackendCi")
    name = "Restaurant Orders CI"
    description = "API and React checks, SQLite/SQL Server concurrency matrix, packaged phone workflows"

    vcs {
        root(DslContext.settingsRoot)
        cleanCheckout = true
    }

    params {
        param("env.DOTNET_CLI_TELEMETRY_OPTOUT", "1")
        param("env.DOTNET_NOLOGO", "1")
    }

    steps {
        for (stage in listOf("Restore", "Check", "Build", "Test", "Publish", "Browser")) {
            script {
                name = stage
                scriptContent = "bash scripts/ci.sh $stage"
            }
        }
    }

    triggers {
        vcs {
            branchFilter = "+:<default>"
        }
    }

    requirements {
        equals("teamcity.agent.jvm.os.name", "Linux")
    }

    failureConditions {
        nonZeroExitCode = true
        testFailure = true
        executionTimeoutMin = 30
    }

    artifactRules = """
        artifacts/api/** => restaurant-orders-app.zip
        artifacts/*-results.xml
        artifacts/*.png
        frontend/test-results/** => browser-test-results.zip
    """.trimIndent()
})
