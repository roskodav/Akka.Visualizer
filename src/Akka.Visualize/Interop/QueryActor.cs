﻿using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Routing;
using Akka.Visualize.Models;
using Akka.Visualize.Utils;

namespace Akka.Visualize.Interop
{
	internal class Messages
	{
		public class Query
		{
			public Query(string path)
			{
				Path = path;
			}

			public string Path { get; }
		}

		public class StopIdentify
		{
			
		}
	}

	internal class QueryActor : ReceiveActor, IWithUnboundedStash
	{
		public IStash Stash { get; set; }

		private IActorRef _lastSender;
		private string _queryPath;
		private string _correlationId;
		private List<NodeInfo> _collectedNodes;

		public QueryActor()
		{
			ReadyState();
		}

		private void ReadyState()
		{
			Receive<Messages.Query>(message =>
			{
				var selector = Context.System.ActorSelection(message.Path);

				_lastSender = Sender;
				_queryPath = message.Path;
				_collectedNodes = new List<NodeInfo>();
				_correlationId = $"Request.{message.Path}.{Environment.TickCount}";

				selector.Tell(new Identify(_correlationId));

				Become(WaitingForIdentifications);

				// max wait is 1s
				Context.System.Scheduler.ScheduleTellOnce(new TimeSpan(0, 0, 1), Self, new Messages.StopIdentify(), Self);
			});
		}

		private void WaitingForIdentifications()
		{
			Receive<Messages.Query>(message => Stash.Stash());

			Receive<ActorIdentity>(message =>
			{
				if (message.MessageId == _correlationId && message.Subject != null)
				{
					_collectedNodes.Add(ToNode(message.Subject));
				}
			});

			Receive<Messages.StopIdentify>(message =>
			{
				// stop and deliver
				_lastSender.Tell(new QueryResult(_queryPath, _collectedNodes));
				Become(ReadyState);
				Stash.UnstashAll();
			});
		}

		private static NodeInfo ToNode(IActorRef actor)
		{
			var node = new NodeInfo()
			{
				Path = actor.Path.ToString(),
				Name = actor.Path.Name
			};
			
			var internalRef = actor as IInternalActorRef;
			if (internalRef != null)
			{
				node.IsLocal = internalRef.IsLocal;
				node.IsTerminated = internalRef.IsTerminated;
			}

			var local = actor as LocalActorRef;
			if (local != null)
			{
				node.Type = local.Cell.Props.Type.FullName;
				node.TypeName = local.Cell.Props.Type.Name;
				node.NoOfMessages = local.Cell.NumberOfMessages;

				var config = local.Cell.Props.RouterConfig;
				config.ToString();
			}

			var repointable = actor as RepointableActorRef;
			if (repointable != null)
			{
				var fa = FieldAccessorCache.Get(repointable.GetType(), "Props");
				var props = (Props)fa.Get(repointable);

				var pool = props.RouterConfig as Pool;
				if (pool != null)
				{
				    node.Router = new RouterInfo
				    {
				        Pool = true,
				        Type = pool.GetType().Name.UpTo("Pool"),
				        NrOfInstances = pool.NrOfInstances
				    };
				}
				else
				{
					var group = props.RouterConfig as Group;
					if (group != null)
					{
                        node.Router = new RouterInfo
                        {
                            Pool = true,
                            Type = group.GetType().Name.UpTo("Group"),
                        };
                    }
				}
			}

			return node;
		}
	}
}
