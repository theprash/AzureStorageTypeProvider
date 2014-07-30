﻿namespace FSharp.Azure.StorageTypeProvider.Queue

open FSharp.Azure.StorageTypeProvider.Queue.QueueRepository
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Queue
open ProviderImplementation.ProvidedTypes
open System

type MessageUpdate = 
    | Visibility
    | VisibilityAndMessage

type ProvidedQueueMessage = 
    { Id : string
      DequeueCount : int
      InsertionTime : DateTimeOffset option
      ExpirationTime : DateTimeOffset option
      NextVisibleTime : DateTimeOffset option
      AsBytes : byte array
      AsString : string
      PopReceipt : string }

module Async = 
    let awaitTaskUnit = Async.AwaitIAsyncResult >> Async.Ignore

module internal Factory = 
    open FSharp.Azure.StorageTypeProvider.Utils
    
    let toProvidedQueueMessage (message : CloudQueueMessage) = 
        { Id = message.Id
          DequeueCount = message.DequeueCount
          InsertionTime = message.InsertionTime |> toOption
          ExpirationTime = message.ExpirationTime |> toOption
          NextVisibleTime = message.NextVisibleTime |> toOption
          AsBytes = message.AsBytes
          AsString = message.AsString
          PopReceipt = message.PopReceipt }
    
    let toAzureQueueMessage message = 
        let msg = CloudQueueMessage(message.Id, message.PopReceipt)
        msg.SetMessageContent message.AsBytes
        msg

type ProvidedQueue(defaultConnectionString, name) = 
    let getConnectionString connection = defaultArg connection defaultConnectionString

    let getQueue = getConnectionString >> getQueueRef name
            
    let enqueue message = getQueue >> (fun q -> q.AddMessageAsync(message) |> Async.awaitTaskUnit)

    /// Gets the queue length.
    member __.GetCurrentLength(?connectionString) = 
        let queueRef = getQueue connectionString
        queueRef.FetchAttributes()
        if queueRef.ApproximateMessageCount.HasValue then queueRef.ApproximateMessageCount.Value
        else 0
    
    /// Dequeues the next message.
    member __.Dequeue(?connectionString) =
        async { 
            let! message = (getQueue connectionString).GetMessageAsync() |> Async.AwaitTask
            return match message with
                   | null -> None
                   | _ -> Some(message |> Factory.toProvidedQueueMessage)
        }

    /// Generates a full-access shared access signature, defaulting to start from now.
    member __.GenerateSharedAccessSignature(duration, ?start, ?connectionString) =
        getQueue connectionString
        |> generateSas start duration
    
    /// Enqueues a new message.
    member __.Enqueue(content : string, ?connectionString) = connectionString |> enqueue(CloudQueueMessage(content))
    
    /// Enqueues a new message.
    member __.Enqueue(content : byte array, ?connectionString) = connectionString |> enqueue(CloudQueueMessage(content))
    
    /// Deletes an existing message.
    member __.Delete(message, ?connectionString) = (connectionString |> getQueue).DeleteMessageAsync(message.Id, message.PopReceipt) |> Async.awaitTaskUnit
    
    /// Updates an existing message.
    member __.Update(message : ProvidedQueueMessage, newTimeout, updateType, ?connectionOverride) = 
        let updateFields = 
            match updateType with
            | Visibility -> MessageUpdateFields.Visibility
            | VisibilityAndMessage -> MessageUpdateFields.Visibility ||| MessageUpdateFields.Content
        (connectionOverride |> getQueue).UpdateMessageAsync(message |> Factory.toAzureQueueMessage, newTimeout, updateFields) |> Async.awaitTaskUnit
    
    /// Gets the name of the queue.
    member __.Name = (None |> getQueue).Name
