__Table of contents__

* [About](#about)


# About

Bud.TaskGraph helps you build dependency graphs of actions and execute them asynchronously and in parallel.

# Example

```csharp
using Bud;

var taskA = new TaskGraph(() => Console.WriteLine("A"));
var taskB = new TaskGraph(() => Console.WriteLine("B"));
var taskC = new TaskGraph(() => Console.WriteLine("C"), taskA, taskB);

// This function blocks. It runs A and B first, and then C.
taskC.Run();

// The asynchronous version of the above call.
await taskC.RunAsync();
```