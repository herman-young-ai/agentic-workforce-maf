// TaskStatus collides with System.Threading.Tasks.TaskStatus brought in by
// ImplicitUsings. Alias once globally so every file reaches the domain enum.
global using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;
