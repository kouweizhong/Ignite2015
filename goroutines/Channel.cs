﻿using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Threading;

public class Channel<T>
{
	readonly int m_bufferCapacity;
	readonly Queue<T> m_bufferQueue;
	readonly Queue<TaskCompletionSource<T>> m_receiverQueue = new Queue<TaskCompletionSource<T>>();
	readonly Queue<Tuple<T, TaskCompletionSource<bool>>> m_senderQueue = new Queue<Tuple<T, TaskCompletionSource<bool>>>();

	public Channel(int bufferSize = 0)
	{
		m_bufferCapacity = bufferSize;
		m_bufferQueue = new Queue<T>(bufferSize);
	}

	/* ——— send with buffer space
	send()
	 -> push a value into the buffer
	 -> return a task which completes immediately

	receive() then does
	 -> pop a value off the buffer
	 -> complete it

	——— send with blocked receiver:
	send()
	 -> pop a value off the receiver queue
	 -> complete it
	 -> return a task which completes immediately

	——— send with no receiver:
	send()
	 -> add a pair<value, task> to the send queue
	 -> return the task which will complete later when someone receives
	 
	receive() then does
	 -> pop a task off the send queue
	 -> push the held value from before into it */
	public Task Send(T value)
	{
		lock(this) {
			if(m_bufferQueue.Count < m_bufferCapacity) {
				m_bufferQueue.Enqueue(value);
				return Task.FromResult(true);
			} else if(m_receiverQueue.Count > 0) {
				var tcs = m_receiverQueue.Dequeue();
				Monitor.Exit(this);
				try {
					tcs.SetResult(value);
				} finally {
					Monitor.Enter(this);
				}
				return Task.FromResult(true);
			} else { // we must block
				var tcs = new TaskCompletionSource<bool>();
				m_senderQueue.Enqueue(Tuple.Create(value, tcs));
				return tcs.Task;
			}
		}
	}

	/* ——— receive with buffered data:
	recieve()
	 -> pop a value off the buffer
	 -> return a task<T> which publishes that value immediately

	——— receive with waiting sender:
	receive()
	 -> pop a pair<task,T> off the sender queue
	 -> complete the task (unblocking the sender)
	 -> return a task which completes immediately with the value

	——— receive with no sender or buffer:
	receive()
	 -> add a task<T> to the receive queue
	 -> return the task which will complete later when someone sends

	send() then does
	-> pop a task off the receive queue
	-> complete it */
	public Task<T> Receive()
	{
		lock(this) {
			if(m_bufferQueue.Count > 0) { // if we have a buffered value, return it
				return Task.FromResult(m_bufferQueue.Dequeue());
			} else if(m_senderQueue.Count > 0) { // we have a blocked sender
				var t = m_senderQueue.Dequeue();
				Monitor.Exit(this);
				try {
					t.Item2.SetResult(false);
				} finally {
					Monitor.Enter(this);
				}
				return Task.FromResult(t.Item1);
			} else { // we must block ourselves
				var tcs = new TaskCompletionSource<T>();
				m_receiverQueue.Enqueue(tcs);
				return tcs.Task;
			}
		}
	}
}