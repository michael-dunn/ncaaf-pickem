using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcaafPickEm.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase0_02_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CfbdId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Abbreviation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Classification = table.Column<byte>(type: "tinyint", nullable: false),
                    EspnGroupId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataRefreshStatus",
                columns: table => new
                {
                    DataType = table.Column<byte>(type: "tinyint", nullable: false),
                    LastSuccessUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataRefreshStatus", x => x.DataType);
                });

            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonWeeks",
                columns: table => new
                {
                    SeasonYear = table.Column<int>(type: "int", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRegularSeason = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonWeeks", x => new { x.SeasonYear, x.Week });
                });

            migrationBuilder.CreateTable(
                name: "UnmatchedGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    RawHomeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RawAwayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GameDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnmatchedGames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoogleSubject = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CfbdId = table.Column<int>(type: "int", nullable: false),
                    School = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Mascot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Abbreviation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ConferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Classification = table.Column<byte>(type: "tinyint", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EspnTeamId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Conferences_ConferenceId",
                        column: x => x.ConferenceId,
                        principalTable: "Conferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SeasonYear = table.Column<int>(type: "int", nullable: false),
                    FirstWeek = table.Column<int>(type: "int", nullable: false),
                    LastWeek = table.Column<int>(type: "int", nullable: false),
                    DefaultPointValue = table.Column<int>(type: "int", nullable: false, defaultValue: 10),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsComplete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leagues_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    EndpointHash = table.Column<byte[]>(type: "binary(32)", nullable: false, computedColumnSql: "CONVERT(binary(32), HASHBYTES('SHA2_256', [Endpoint]))", stored: true),
                    P256dh = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CfbdGameId = table.Column<long>(type: "bigint", nullable: false),
                    EspnEventId = table.Column<long>(type: "bigint", nullable: true),
                    SeasonYear = table.Column<int>(type: "int", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KickoffUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KickoffEasternDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsSaturdayEastern = table.Column<bool>(type: "bit", nullable: false),
                    IsConferenceGame = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: true),
                    AwayScore = table.Column<int>(type: "int", nullable: true),
                    Period = table.Column<byte>(type: "tinyint", nullable: true),
                    Clock = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Venue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastScoreUpdateUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rankings",
                columns: table => new
                {
                    SeasonYear = table.Column<int>(type: "int", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    Poll = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rankings", x => new { x.SeasonYear, x.Week, x.Poll, x.Rank });
                    table.ForeignKey(
                        name: "FK_Rankings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamAliases_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GameSetRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: true),
                    RuleType = table.Column<byte>(type: "tinyint", nullable: false),
                    ConferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConferenceGamesOnly = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSetRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSetRules_Conferences_ConferenceId",
                        column: x => x.ConferenceId,
                        principalTable: "Conferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameSetRules_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameSetRules_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    DisplayNameOverride = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    JoinedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JoinedWeek = table.Column<int>(type: "int", nullable: false),
                    RemovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memberships_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Memberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PointRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RuleType = table.Column<byte>(type: "tinyint", nullable: false),
                    ConferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SpreadThreshold = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    PointValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointRules_Conferences_ConferenceId",
                        column: x => x.ConferenceId,
                        principalTable: "Conferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PointRules_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PointRules_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeekGameSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    UsesOverride = table.Column<bool>(type: "bit", nullable: false),
                    GeneratedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsComplete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekGameSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeekGameSets_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Result = table.Column<byte>(type: "tinyint", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationLog_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLog_PushSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "PushSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLog_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GameLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Spread = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameLines_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLog_Memberships_ActorMembershipId",
                        column: x => x.ActorMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedByMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: false, defaultValue: 50),
                    Uses = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invites_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invites_Memberships_CreatedByMembershipId",
                        column: x => x.CreatedByMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeasonStandingsSnapshots",
                columns: table => new
                {
                    LeagueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThroughWeek = table.Column<int>(type: "int", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    TotalPoints = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonStandingsSnapshots", x => new { x.LeagueId, x.ThroughWeek, x.MembershipId });
                    table.ForeignKey(
                        name: "FK_SeasonStandingsSnapshots_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonStandingsSnapshots_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeekGameSetGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekGameSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    RemovedReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PointValueOverride = table.Column<int>(type: "int", nullable: true),
                    ResolvedPointValue = table.Column<int>(type: "int", nullable: false),
                    SpreadAtLock = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    ResultOverrideWinnerTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekGameSetGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeekGameSetGames_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeekGameSetGames_Teams_ResultOverrideWinnerTeamId",
                        column: x => x.ResultOverrideWinnerTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeekGameSetGames_WeekGameSets_WeekGameSetId",
                        column: x => x.WeekGameSetId,
                        principalTable: "WeekGameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeekResults",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekGameSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    ActiveGameCount = table.Column<int>(type: "int", nullable: false),
                    IsWeekComplete = table.Column<bool>(type: "bit", nullable: false),
                    ComputedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekResults", x => new { x.MembershipId, x.WeekGameSetId });
                    table.ForeignKey(
                        name: "FK_WeekResults_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeekResults_WeekGameSets_WeekGameSetId",
                        column: x => x.WeekGameSetId,
                        principalTable: "WeekGameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeekSubmissions",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekGameSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HasUnseenGameChanges = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekSubmissions", x => new { x.MembershipId, x.WeekGameSetId });
                    table.ForeignKey(
                        name: "FK_WeekSubmissions_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeekSubmissions_WeekGameSets_WeekGameSetId",
                        column: x => x.WeekGameSetId,
                        principalTable: "WeekGameSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Picks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekGameSetGameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickedTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Picks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Picks_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Picks_Teams_PickedTeamId",
                        column: x => x.PickedTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Picks_WeekGameSetGames_WeekGameSetGameId",
                        column: x => x.WeekGameSetGameId,
                        principalTable: "WeekGameSetGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActorMembershipId",
                table: "AuditLog",
                column: "ActorMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_LeagueId_CreatedUtc",
                table: "AuditLog",
                columns: new[] { "LeagueId", "CreatedUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Conferences_CfbdId",
                table: "Conferences",
                column: "CfbdId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameLines_GameId_FetchedUtc",
                table: "GameLines",
                columns: new[] { "GameId", "FetchedUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Games_AwayTeamId",
                table: "Games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_CfbdGameId",
                table: "Games",
                column: "CfbdGameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_EspnEventId",
                table: "Games",
                column: "EspnEventId",
                unique: true,
                filter: "[EspnEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Games_HomeTeamId",
                table: "Games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_SeasonYear_Week_IsSaturdayEastern",
                table: "Games",
                columns: new[] { "SeasonYear", "Week", "IsSaturdayEastern" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_Status",
                table: "Games",
                column: "Status",
                filter: "[Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_GameSetRules_ConferenceId",
                table: "GameSetRules",
                column: "ConferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSetRules_LeagueId_Week_SortOrder",
                table: "GameSetRules",
                columns: new[] { "LeagueId", "Week", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GameSetRules_TeamId",
                table: "GameSetRules",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Invites_Code",
                table: "Invites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invites_CreatedByMembershipId",
                table: "Invites",
                column: "CreatedByMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Invites_LeagueId",
                table: "Invites",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_JobName_ScheduledForUtc",
                table: "JobRuns",
                columns: new[] { "JobName", "ScheduledForUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_StartedUtc",
                table: "JobRuns",
                column: "StartedUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_CreatedByUserId",
                table: "Leagues",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_SeasonYear",
                table: "Leagues",
                column: "SeasonYear");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_LeagueId_RemovedUtc",
                table: "Memberships",
                columns: new[] { "LeagueId", "RemovedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_LeagueId_UserId",
                table: "Memberships",
                columns: new[] { "LeagueId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLog_LeagueId_CreatedUtc",
                table: "NotificationLog",
                columns: new[] { "LeagueId", "CreatedUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLog_SubscriptionId",
                table: "NotificationLog",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationLog_WeeklyReminderOncePerWeek",
                table: "NotificationLog",
                columns: new[] { "UserId", "LeagueId", "Week", "Type" },
                unique: true,
                filter: "[Type] IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_Picks_MembershipId_WeekGameSetGameId",
                table: "Picks",
                columns: new[] { "MembershipId", "WeekGameSetGameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Picks_PickedTeamId",
                table: "Picks",
                column: "PickedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Picks_WeekGameSetGameId",
                table: "Picks",
                column: "WeekGameSetGameId");

            migrationBuilder.CreateIndex(
                name: "IX_PointRules_ConferenceId",
                table: "PointRules",
                column: "ConferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_PointRules_LeagueId_Priority",
                table: "PointRules",
                columns: new[] { "LeagueId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_PointRules_TeamId",
                table: "PointRules",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_Provider_StartedUtc",
                table: "ProviderCalls",
                columns: new[] { "Provider", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_EndpointHash",
                table: "PushSubscriptions",
                column: "EndpointHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Rankings_SeasonYear_Week_TeamId",
                table: "Rankings",
                columns: new[] { "SeasonYear", "Week", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Rankings_TeamId",
                table: "Rankings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonStandingsSnapshots_MembershipId",
                table: "SeasonStandingsSnapshots",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamAliases_Source_Alias",
                table: "TeamAliases",
                columns: new[] { "Source", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamAliases_TeamId",
                table: "TeamAliases",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_CfbdId",
                table: "Teams",
                column: "CfbdId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Classification",
                table: "Teams",
                column: "Classification");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ConferenceId",
                table: "Teams",
                column: "ConferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_EspnTeamId",
                table: "Teams",
                column: "EspnTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_UnmatchedGames_ResolvedUtc_GameDate",
                table: "UnmatchedGames",
                columns: new[] { "ResolvedUtc", "GameDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleSubject",
                table: "Users",
                column: "GoogleSubject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeekGameSetGames_GameId",
                table: "WeekGameSetGames",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_WeekGameSetGames_ResultOverrideWinnerTeamId",
                table: "WeekGameSetGames",
                column: "ResultOverrideWinnerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_WeekGameSetGames_WeekGameSetId_GameId",
                table: "WeekGameSetGames",
                columns: new[] { "WeekGameSetId", "GameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeekGameSets_LeagueId_Week",
                table: "WeekGameSets",
                columns: new[] { "LeagueId", "Week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeekGameSets_LockAtUtc_LockedUtc",
                table: "WeekGameSets",
                columns: new[] { "LockAtUtc", "LockedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WeekResults_WeekGameSetId",
                table: "WeekResults",
                column: "WeekGameSetId");

            migrationBuilder.CreateIndex(
                name: "IX_WeekSubmissions_WeekGameSetId",
                table: "WeekSubmissions",
                column: "WeekGameSetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "DataRefreshStatus");

            migrationBuilder.DropTable(
                name: "GameLines");

            migrationBuilder.DropTable(
                name: "GameSetRules");

            migrationBuilder.DropTable(
                name: "Invites");

            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropTable(
                name: "NotificationLog");

            migrationBuilder.DropTable(
                name: "Picks");

            migrationBuilder.DropTable(
                name: "PointRules");

            migrationBuilder.DropTable(
                name: "ProviderCalls");

            migrationBuilder.DropTable(
                name: "Rankings");

            migrationBuilder.DropTable(
                name: "SeasonStandingsSnapshots");

            migrationBuilder.DropTable(
                name: "SeasonWeeks");

            migrationBuilder.DropTable(
                name: "TeamAliases");

            migrationBuilder.DropTable(
                name: "UnmatchedGames");

            migrationBuilder.DropTable(
                name: "WeekResults");

            migrationBuilder.DropTable(
                name: "WeekSubmissions");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "WeekGameSetGames");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "WeekGameSets");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "Conferences");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
