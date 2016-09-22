using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Bud {
  /// <summary>
  ///   A wrapper around <see cref="Task" />
  /// </summary>
  public class TaskGraph {
    /// <summary>
    ///   The action that this task will execute.
    /// </summary>
    public Action Action { get; }

    /// <summary>
    ///   The task on which this task depends. These will be executed before this task.
    /// </summary>
    public ImmutableArray<TaskGraph> Dependencies { get; }

    /// <summary>
    ///   Creates a task graph with a root action and some dependency actions.
    /// </summary>
    /// <param name="action">the action to be invoked after all the dependencies have executed.</param>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(Action action, params TaskGraph[] dependencies) : this(action, ImmutableArray.CreateRange(dependencies)) {}

    /// <summary>
    ///   Creates a task graph with a single action (without dependencies).
    /// </summary>
    /// <param name="action">the action to be invoked.</param>
    public TaskGraph(Action action) : this(action, ImmutableArray<TaskGraph>.Empty) {}

    /// <summary>
    ///   Creates a task graph with a root action and some dependency actions.
    /// </summary>
    /// <param name="action">the action to be invoked after all the dependencies have executed.</param>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(Action action, ImmutableArray<TaskGraph> dependencies) {
      Action = action;
      Dependencies = dependencies;
    }

    internal TaskGraph(ImmutableArray<TaskGraph> subGraphs) : this(null, subGraphs) {}

    /// <summary>
    ///   Executes all tasks in this graph in parallel (using the <see cref="Task" /> API).
    ///   This method blocks.
    /// </summary>
    public void Run() => RunAsync().Wait();

    /// <summary>
    ///   Asynchronously executes all tasks in this graph in parallel (using the <see cref="Task" /> API).
    /// </summary>
    public async Task RunAsync() => await ToTask(new Dictionary<TaskGraph, Task>());

    /// <summary>
    ///   Converts objects of type <typeparamref name="T" /> into a task graph. Each object must have a name. The name is
    ///   retrieved via the given <paramref name="nameOfTask" /> callback function. Tasks with the same name are considered
    ///   identical. Dependencies of an object are retrieved via the <paramref name="dependenciesOfTask" /> function. If
    ///   multiple tasks depend on tasks with the same, then they will share the tasks graph node and its action will be
    ///   invoked only once. The action of a task is retrieved via the <paramref name="actionOfTask" /> function.
    /// </summary>
    /// <typeparam name="T">the type of objects to convert to a task graph.</typeparam>
    /// <param name="rootTasks">the task objects from which to start building the task graph.</param>
    /// <param name="nameOfTask">the function that returns the name of the given task object.</param>
    /// <param name="dependenciesOfTask">the function that returns task objects on which the given task object depends.</param>
    /// <param name="actionOfTask">the function that returns the action for the given task object.</param>
    /// <returns>a task graph that can be executed.</returns>
    public static TaskGraph ToTaskGraph<T>(IEnumerable<T> rootTasks,
                                           Func<T, string> nameOfTask,
                                           Func<T, IEnumerable<T>> dependenciesOfTask,
                                           Func<T, Action> actionOfTask)
      => new TaskGraphBuilder<T>(nameOfTask, dependenciesOfTask, actionOfTask).ToTaskGraph(rootTasks);

    private Task ToTask(IDictionary<TaskGraph, Task> existingTasks) {
      Task task;
      if (existingTasks.TryGetValue(this, out task)) {
        return task;
      }
      if (Dependencies.Length <= 0) {
        task = Action == null ? Task.CompletedTask : Task.Run(Action);
      } else {
        task = Task.WhenAll(Dependencies.Select(tg => tg.ToTask(existingTasks)));
        task = Action == null ? task : task.ContinueWith(t => Action());
      }
      existingTasks.Add(this, task);
      return task;
    }

    private class TaskGraphBuilder<TTask> {
      private readonly Func<TTask, IEnumerable<TTask>> dependenciesOfTask;
      private readonly Func<TTask, Action> actionOfTask;
      private readonly Func<TTask, string> nameOfTask;
      private readonly IDictionary<string, TaskGraph> finishedTasks;
      private readonly HashSet<string> dependencyChain;
      private readonly List<string> orderedDependencyChain;

      public TaskGraphBuilder(Func<TTask, string> nameOfTask,
                              Func<TTask, IEnumerable<TTask>> dependenciesOfTask,
                              Func<TTask, Action> actionOfTask) {
        this.dependenciesOfTask = dependenciesOfTask;
        this.actionOfTask = actionOfTask;
        this.nameOfTask = nameOfTask;
        finishedTasks = new Dictionary<string, TaskGraph>();
        dependencyChain = new HashSet<string>();
        orderedDependencyChain = new List<string>();
      }

      public TaskGraph ToTaskGraph(IEnumerable<TTask> tasks) => new TaskGraph(ToTaskGraphs(tasks));

      private ImmutableArray<TaskGraph> ToTaskGraphs(IEnumerable<TTask> tasks)
        => tasks.Select(ToTaskGraph).ToImmutableArray();

      private TaskGraph ToTaskGraph(TTask task) {
        var taskName = nameOfTask(task);
        TaskGraph cachedTaskGraph;
        if (finishedTasks.TryGetValue(taskName, out cachedTaskGraph)) {
          return cachedTaskGraph;
        }
        EnterTask(taskName);
        var dependencyTasks = ToTaskGraphs(dependenciesOfTask(task));
        LeaveTask(taskName);
        return CreateTaskGraph(task, taskName, dependencyTasks);
      }

      private void EnterTask(string taskName) {
        if (dependencyChain.Contains(taskName)) {
          throw new Exception("Detected a dependency cycle: " +
                              $"'{string.Join(" depends on ", orderedDependencyChain)} " +
                              $"depends on {taskName}'.");
        }
        dependencyChain.Add(taskName);
        orderedDependencyChain.Add(taskName);
      }

      private void LeaveTask(string taskName) {
        dependencyChain.Remove(taskName);
        orderedDependencyChain.RemoveAt(orderedDependencyChain.Count - 1);
      }

      private TaskGraph CreateTaskGraph(TTask task, string taskName, ImmutableArray<TaskGraph> dependencyTasks) {
        var thisTaskGraph = new TaskGraph(actionOfTask(task), dependencyTasks);
        finishedTasks.Add(taskName, thisTaskGraph);
        return thisTaskGraph;
      }
    }
  }
}