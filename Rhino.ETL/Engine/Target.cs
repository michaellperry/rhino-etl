namespace Rhino.ETL.Engine
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Transactions;
	using Boo.Lang;
	using Boo.Lang.Compiler.MetaProgramming;
	using Commands;
	using Exceptions;
	using Retlang;

	public abstract class Target : ContextfulObjectBase<Target>
	{
		private readonly string name;
		private TimeSpan timeOut = TimeSpan.FromHours(2);
		private ICommandContainer container;
		private readonly List<Exception> exceptions = new List<Exception>();
		private IProcessContext listenToContainer;

		public Target(string name)
		{
			this.name = name;
			EtlConfigurationContext.Current.AddTarget(name, this);
			container = new ExecuteInParallelCommand(this);
		}

		public override string Name
		{
			get { return name; }
		}

		[ReadOnly(false)]
		public TimeSpan TimeOut
		{
			get { return timeOut; }
			set { timeOut = value; }
		}

		public IList<ICommand> Commands
		{
			get { return container.Commands; }
		}

		public bool IsFaulted
		{
			get { return exceptions.Count != 0; }
		}

		public ICommand Execute(string pipelineName)
		{
			Pipeline pipeline;
			if (EtlConfigurationContext.Current.Pipelines.TryGetValue(pipelineName, out pipeline) == false)
				throw new InvalidPipelineException("Could not find pipeline '" + pipelineName + "'");
			ExecutePipeline ep = new ExecutePipeline(this, pipeline);
			AddCommand(ep);
			return ep;
		}

		private void AddCommand(ICommand command)
		{
			container.Add(command);
		}

		[Meta]
		public void transaction(ICallable callable)
		{
			Transaction(callable);
		}

		[Meta]
		public void Transaction(ICallable callable)
		{
			ExecuteInParallelTransactionCommand command = new ExecuteInParallelTransactionCommand(this);
			RunUnderDifferentCommandContainerContext(command, command, callable);
		}

		[Meta]
		public void transaction(IsolationLevel isolationLevel,ICallable callable)
		{
			Transaction(isolationLevel,callable);
		}

		[Meta]
		public void Transaction(IsolationLevel isolationLevel, ICallable callable)
		{
			ExecuteInParallelTransactionCommand command = new ExecuteInParallelTransactionCommand(this,isolationLevel);
			RunUnderDifferentCommandContainerContext(command, command, callable);
		}

		[Meta]
		public void parallel(ICallable callable)
		{
			Parallel(callable);
		}

		[Meta]
		public void Parallel(ICallable callable)
		{
			ExecuteInParallelCommand command = new ExecuteInParallelCommand(this);
			RunUnderDifferentCommandContainerContext(command, command, callable);
		}

		[Meta]
		public void sequence(ICallable callable)
		{
			Sequence(callable);
		}

		[Meta]
		public void Sequence(ICallable callable)
		{
			ExecuteInSequenceCommand command = new ExecuteInSequenceCommand(this);
			RunUnderDifferentCommandContainerContext(command, command, callable);
		}

		private void RunUnderDifferentCommandContainerContext(
			ICommand command,
			ICommandContainer newContainer,
			ICallable callable)
		{
			ICommandContainer old = container;
			container = newContainer;
			callable.Call(new object[0]);
			container = old;
			container.Add(command);
		}

		public abstract void Prepare();

		public void Run(IProcessContextFactory contextFactory)
		{
			listenToContainer = contextFactory.CreateAndStart();
			this.listenToContainer.Subscribe<Exception>(new TopicEquals(Messages.Exception),
			delegate(IMessageHeader header, Exception msg)
			{
				AddFault(msg);
			});
			container.Execute(contextFactory);
		}

		public void WaitForCompletion()
		{
			if(container.WaitForCompletion(TimeOut) == false)
			{
				string format = string.Format("The time out period for {0} have passed! {1} has passed and the process is still running", Name, TimeOut);
				TimeoutException e = new TimeoutException(format);
				AddFault(e);
				throw e;
			}
			listenToContainer.Stop();
		}

		public void AddFault(Exception e)
		{
			exceptions.Add(e);
			container.ForceEndOfCompletionWithoutFurtherWait();
		}

		public ExecutionResult GetExecutionResult(EtlConfigurationContext configurationContext)
		{
			return new ExecutionResult(exceptions, 
				IsFaulted ? ExecutionStatus.Failure : ExecutionStatus.Success, configurationContext.Errors);
		}

		public void ForceEnd()
		{
			container.ForceEndOfCompletionWithoutFurtherWait();
		}
	}
}