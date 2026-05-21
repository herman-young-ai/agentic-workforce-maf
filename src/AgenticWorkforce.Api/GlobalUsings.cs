// Disambiguate our domain TaskStatus from System.Threading.Tasks.TaskStatus
// across all task feature files.
global using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AgenticWorkforce.Api.Tests.Unit")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AgenticWorkforce.Api.Tests.Integration")]
