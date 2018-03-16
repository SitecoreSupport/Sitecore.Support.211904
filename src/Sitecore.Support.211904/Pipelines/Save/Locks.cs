﻿namespace Sitecore.Support.Pipelines.Save
{
  using System;
  using Sitecore.Configuration;
  using Sitecore.Data;
  using Sitecore.Data.Items;
  using Sitecore.Data.Locking;
  using Sitecore.Diagnostics;
  using Sitecore.Data.Managers;
  using Sitecore.Pipelines.Save;

  /// <summary>
  /// Locks the item.
  /// </summary>
  public class Lock
  {
    #region Public methods

    /// <summary>
    /// Runs the processor.
    /// </summary>
    /// <param name="args">
    /// The arguments.
    /// </param>
    public void Process([NotNull] SaveArgs args)
    {
      Assert.ArgumentNotNull(args, "args");

      if (!args.PolicyBasedLocking && !Settings.AutomaticLockOnSave)
      {
        return;
      }

      foreach (var saveItem in args.Items)
      {
        var item = Client.ContentDatabase.Items[saveItem.ID, saveItem.Language, saveItem.Version];
        if (item == null)
        {
          continue;
        }

        string itemName = item.Name;
        if (!item.Locking.HasLock())
        {
          try
          {
            if (item.Locking.IsLocked())
            {
              if (!Context.User.IsAdministrator)
              {
                item = null;
              }
            }
            else
            {
              if (!(args.PolicyBasedLocking && Context.User.IsInRole(Constants.MinimalPageEditorRoleName)))
              {
                item = this.StartEditing(item);
                if (Settings.AutomaticLockOnSave && !item.Locking.IsLocked())
                {
                  item.Locking.Lock();
                }
              }
              else
              {
                using (new LockingDisabler())
                {
                  item = this.StartEditing(item);
                }
              }
            }

            if (item == null)
            {
              args.Error = "Could not lock the item \"" + itemName + "\"";
              args.AbortPipeline();
            }
            else
            {
              SaveArgs.SaveField lockField = this.GetField(saveItem, FieldIDs.Lock);
              if (lockField != null)
              {
                string lockValue = item[FieldIDs.Lock];
                if (string.Compare(lockField.Value, lockValue, StringComparison.InvariantCultureIgnoreCase) != 0)
                {
                  lockField.Value = lockValue;
                }
              }

              saveItem.Version = item.Version;
            }
          }
          catch (Exception e)
          {
            args.Error = e.Message;
            args.AbortPipeline();
          }
        }
      }
    }

    #endregion

    #region Protected methods

    /// <summary>
    /// Gets the field.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="fieldID">The field ID.</param>
    /// <returns>The field.</returns>
    [CanBeNull]
    protected virtual SaveArgs.SaveField GetField([NotNull] SaveArgs.SaveItem item, [NotNull] ID fieldID)
    {
      Assert.ArgumentNotNull(item, "item");
      Assert.ArgumentNotNull(fieldID, "fieldID");

      foreach (SaveArgs.SaveField field in item.Fields)
      {
        if (field.ID == fieldID)
        {
          return field;
        }
      }

      return null;
    }

    #endregion

    #region Private Methods
    private Item StartEditing(Item item)
    {
      Error.AssertObject(item, "item");

      var _context = Sitecore.Context.Data;

      if (!Settings.RequireLockBeforeEditing || Sitecore.Context.User.IsAdministrator) // Sitecore.Support.211904
      {
        return item;
      }

      if (_context.IsAdministrator)
      {
        return WorkflowContextLock(item);
      }

      if (StandardValuesManager.IsStandardValuesHolder(item))
      {
        return WorkflowContextLock(item);
      }

      if (!_context.Workflow.HasWorkflow(item) && !_context.Workflow.HasDefaultWorkflow(item))
      {
        return WorkflowContextLock(item);
      }

      if (!_context.Workflow.IsApproved(item))
      {
        return WorkflowContextLock(item);
      }

      // item has been approved, create new version
      Item newVersion = item.Versions.AddVersion();

      if (newVersion != null)
      {
        return WorkflowContextLock(newVersion);
      }

      return null;
    }


    private Item WorkflowContextLock(Item item)
    {
      if (TemplateManager.IsFieldPartOfTemplate(FieldIDs.Lock, item))
      {
        if (!item.Locking.Lock())
        {
          return null;
        }
      }

      return item;
    }

    #endregion

  }
}