// AgenticWorkforce.Domain.Enums.TaskStatus shadows System.Threading.Tasks.TaskStatus
// (both reachable via implicit usings). Aliasing project-wide keeps every test file
// referring to the domain enum without a per-file using-alias.
global using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;
