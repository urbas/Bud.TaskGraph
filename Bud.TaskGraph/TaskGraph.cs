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
    public TaskGraph(Action action, ImmutableArray<TaskGraph> dependencies) {
      Action = action;
      Dependencies = dependencies;
    }

    /// <summary>
    ///   Creates a task graph with a root action and some dependency actions.
    /// </summary>
    /// <param name="action">the action to be invoked after all the dependencies have executed.</param>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(Action action, params TaskGraph[] dependencies)
      : this(action, ImmutableArray.CreateRange(dependencies)) { }

    /// <summary>
    ///   Creates a task graph without any action and some dependencies.
    /// </summary>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(params TaskGraph[] dependencies) : this(null, ImmutableArray.CreateRange(dependencies)) { }

    /// <summary>
    ///   Creates a task graph with a root action and some dependency actions.
    /// </summary>
    /// <param name="action">the action to be invoked after all the dependencies have executed.</param>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(Action action, IEnumerable<TaskGraph> dependencies)
      : this(action, ImmutableArray.CreateRange(dependencies)) { }

    /// <summary>
    ///   Creates a task graph without any action and some dependencies.
    /// </summary>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(ImmutableArray<TaskGraph> dependencies) : this(null, dependencies) { }

    /// <summary>
    ///   Creates a task graph without any action and some dependencies.
    /// </summary>
    /// <param name="dependencies">the dependencies to execute before executing the action.</param>
    /// <remarks>There is no guarantee on the order of execution of dependencies.</remarks>
    public TaskGraph(IEnumerable<TaskGraph> dependencies) : this(null, ImmutableArray.CreateRange(dependencies)) { }

    /// <summary>
    ///   Creates a task graph with a single action (without dependencies).
    /// </summary>
    /// <param name="action">the action to be invoked.</param>
    public TaskGraph(Action action) : this(action, ImmutableArray<TaskGraph>.Empty) { }

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
    ///   Creates a task graph in which the given <paramref name="parallelActions"/> should be executed
    ///   in parallel.
    /// </summary>
    /// <param name="parallelActions">
    ///   the actions that should be executed in parallel.
    /// </param>
    /// <returns>
    ///   a task graph whose dependencies are  the given <paramref name="parallelActions" />.
    /// </returns>
    public static TaskGraph ToTaskGraph(params Action[] parallelActions)
      => new TaskGraph(parallelActions.Select(action => new TaskGraph(action)));

    /// <summary>
    ///   Converts objects of type <typeparamref name="T" /> into a task graph. Each object must have a name. The name is
    ///   retrieved via the given <paramref name="nameOfTask" /> callback function. Tasks with the same name are considered
    ///   identical. Dependencies of an object are retrieved via the <paramref name="dependenciesOfTask" /> function. If
    ///   multiple tasks depend on tasks with the same, then they will share the tasks graph node and its action will be
    ///   invoked only once. The action of a task is retrieved via the <paramref name="actionOfTask" /> function.
    /// </summary>
    /// <typeparam name="T">the type of objects to convert to a task graph.</typeparam>
    /// <param name="rootTasks">the task objects from which to start building the task graph.</param>
    /// <param name="nameOfTask">the function that returns the name of the given task object. Note that tasks must have
    /// unique names.</param>
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
      private readonly Func<TTask, string> nameOfTask;
      private readonly Func<TTask, Action> actionOfTask;
      private readonly Func<TTask, IEnumerable<TTask>> dependenciesOfTask;
      private readonly HashSet<string> taskNames = new HashSet<string>();
      private readonly HashSet<TTask> dependencyChain = new HashSet<TTask>();
      private readonly List<string> orderedDependencyChain = new List<string>();
      private readonly IDictionary<TTask, TaskGraph> taskToTaskGraph = new Dictionary<TTask, TaskGraph>();

      public TaskGraphBuilder(Func<TTask, string> nameOfTask,
                              Func<TTask, IEnumerable<TTask>> dependenciesOfTask,
                              Func<TTask, Action> actionOfTask) {
        this.dependenciesOfTask = dependenciesOfTask;
        this.actionOfTask = actionOfTask;
        this.nameOfTask = nameOfTask;
      }

      public TaskGraph ToTaskGraph(IEnumerable<TTask> tasks) => new TaskGraph(ToTaskGraphs(tasks));

      private ImmutableArray<TaskGraph> ToTaskGraphs(IEnumerable<TTask> tasks)
        => tasks.Select(ToTaskGraph).ToImmutableArray();

      private TaskGraph ToTaskGraph(TTask task) {
        TaskGraph cachedTaskGraph;
        if (taskToTaskGraph.TryGetValue(task, out cachedTaskGraph)) {
          return cachedTaskGraph;
        }
        PushTaskOnStack(task);
        var dependencyTasks = ToTaskGraphs(dependenciesOfTask(task));
        PopTaskFromStack(task);
        return CreateTaskGraph(task, dependencyTasks);
      }

      private void PushTaskOnStack(TTask task) {
        var taskName = nameOfTask(task);
        AssertNoCycles(task, taskName);
        AssertNoNameClashes(taskName);
        dependencyChain.Add(task);
        orderedDependencyChain.Add(taskName);
      }

      private void AssertNoNameClashes(string taskName) {
        if (taskNames.Remove(taskName)) {
          throw new Exception($"Detected multiple tasks with the name '{taskName}'. Tasks must have unique names.");
        }
        taskNames.Add(taskName);
      }

      private void AssertNoCycles(TTask task, string taskName) {
        if (dependencyChain.Contains(task)) {
          throw new Exception("Detected a dependency cycle: " +
                              $"'{string.Join(" depends on ", orderedDependencyChain)} " +
                              $"depends on {taskName}'.");
        }
      }

      private void PopTaskFromStack(TTask task) {
        dependencyChain.Remove(task);
        orderedDependencyChain.RemoveAt(orderedDependencyChain.Count - 1);
      }

      private TaskGraph CreateTaskGraph(TTask task, ImmutableArray<TaskGraph> dependencyTasks) {
        var thisTaskGraph = new TaskGraph(actionOfTask(task), dependencyTasks);
        taskToTaskGraph.Add(task, thisTaskGraph);
        return thisTaskGraph;
      }
    }
  }
}