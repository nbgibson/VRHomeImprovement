﻿// Pointer|Pointers|10020
namespace VRTK
{
    using UnityEngine;
#if UNITY_5_5_OR_NEWER
    using UnityEngine.AI;
#endif

    /// <summary>
    /// The VRTK Pointer class forms the basis of being able to emit a pointer from a game object (e.g. controller).
    /// </summary>
    /// <remarks>
    /// The concept of the pointer is it can be activated and deactivated and used to select elements utilising different button combinations if required.
    ///
    /// The Pointer requires a Pointer Renderer which is the visualisation of the pointer in the scene.
    ///
    /// A Pointer can also be used to extend the interactions of an interacting object such as a controller. This enables pointers to touch (and highlight), grab and use interactable objects.
    ///
    /// The Pointer script does not need to go on a controller game object, but if it's placed on another object then a controller must be provided to determine what activates the pointer.
    ///
    /// It extends the `VRTK_DestinationMarker` to allow for destination events to be emitted when the pointer cursor collides with objects.
    /// </remarks>
    public class VRTK_Pointer : VRTK_DestinationMarker
    {
        [Header("Pointer Activation Settings")]

        [Tooltip("The specific renderer to use when the pointer is activated. The renderer also determines how the pointer reaches it's destination (e.g. straight line, bezier curve).")]
        public VRTK_BasePointerRenderer pointerRenderer;
        [Tooltip("The button used to activate/deactivate the pointer.")]
        public VRTK_ControllerEvents.ButtonAlias activationButton = VRTK_ControllerEvents.ButtonAlias.TouchpadPress;
        [Tooltip("If this is checked then the Activation Button needs to be continuously held down to keep the pointer active. If this is unchecked then the Activation Button works as a toggle, the first press/release enables the pointer and the second press/release disables the pointer.")]
        public bool holdButtonToActivate = true;
        [Tooltip("If this is checked then the pointer will be toggled on when the script is enabled.")]
        public bool activateOnEnable = false;
        [Tooltip("The time in seconds to delay the pointer being able to be active again.")]
        public float activationDelay = 0f;

        [Header("Pointer Selection Settings")]

        [Tooltip("The button used to execute the select action at the pointer's target position.")]
        public VRTK_ControllerEvents.ButtonAlias selectionButton = VRTK_ControllerEvents.ButtonAlias.TouchpadPress;
        [Tooltip("If this is checked then the pointer selection action is executed when the Selection Button is pressed down. If this is unchecked then the selection action is executed when the Selection Button is released.")]
        public bool selectOnPress = false;
        [Tooltip("The time in seconds to delay the pointer being able to execute the select action again.")]
        public float selectionDelay = 0f;
        [Tooltip("The amount of time the pointer can be over the same collider before it automatically attempts to select it. 0f means no selection attempt will be made.")]
        public float selectAfterHoverDuration = 0f;

        [Header("Pointer Interaction Settings")]

        [Tooltip("If this is checked then the pointer will be an extension of the controller and able to interact with Interactable Objects.")]
        public bool interactWithObjects = false;
        [Tooltip("If `Interact With Objects` is checked and this is checked then when an object is grabbed with the pointer touching it, the object will attach to the pointer tip and not snap to the controller.")]
        public bool grabToPointerTip = false;

        [Header("Pointer Customisation Settings")]

        [Tooltip("The controller that will be used to toggle the pointer. If the script is being applied onto a controller then this parameter can be left blank as it will be auto populated by the controller the script is on at runtime.")]
        public VRTK_ControllerEvents controller;
        [Tooltip("A custom transform to use as the origin of the pointer. If no pointer origin transform is provided then the transform the script is attached to is used.")]
        public Transform customOrigin;
        [Tooltip("A custom VRTK_PointerDirectionIndicator to use to determine the rotation given to the destination set event.")]
        public VRTK_PointerDirectionIndicator directionIndicator;

        protected VRTK_ControllerEvents.ButtonAlias subscribedActivationButton = VRTK_ControllerEvents.ButtonAlias.Undefined;
        protected VRTK_ControllerEvents.ButtonAlias subscribedSelectionButton = VRTK_ControllerEvents.ButtonAlias.Undefined;
        protected bool currentSelectOnPress;
        protected float activateDelayTimer;
        protected float selectDelayTimer;
        protected float hoverDurationTimer;
        protected int currentActivationState;
        protected bool willDeactivate;
        protected bool wasActivated;
        protected uint controllerIndex;
        protected VRTK_InteractableObject pointerInteractableObject = null;
        protected Collider currentCollider;
        protected bool canClickOnHover;

        /// <summary>
        /// The PointerEnter method emits a DestinationMarkerEnter event when the pointer enters a valid object.
        /// </summary>
        /// <param name="givenHit">The valid collision.</param>
        public virtual void PointerEnter(RaycastHit givenHit)
        {
            if (enabled && givenHit.transform && controllerIndex < uint.MaxValue)
            {
                SetHoverSelectionTimer(givenHit.collider);
                OnDestinationMarkerEnter(SetDestinationMarkerEvent(givenHit.distance, givenHit.transform, givenHit, givenHit.point, controllerIndex, false, GetCursorRotation()));
                StartUseAction(givenHit.transform);
            }
        }

        /// <summary>
        /// The PointerExit method emits a DestinationMarkerExit event when the pointer leaves a previously entered object.
        /// </summary>
        /// <param name="givenHit">The previous valid collision.</param>
        public virtual void PointerExit(RaycastHit givenHit)
        {
            ResetHoverSelectionTimer(givenHit.collider);
            if (givenHit.transform && controllerIndex < uint.MaxValue)
            {
                OnDestinationMarkerExit(SetDestinationMarkerEvent(givenHit.distance, givenHit.transform, givenHit, givenHit.point, controllerIndex, false, GetCursorRotation()));
                StopUseAction();
            }
        }

        /// <summary>
        /// The CanActivate method is used to determine if the pointer has passed the activation time limit.
        /// </summary>
        /// <returns>Returns true if the pointer can be activated.</returns>
        public virtual bool CanActivate()
        {
            return (Time.time >= activateDelayTimer);
        }

        /// <summary>
        /// The CanSelect method is used to determine if the pointer has passed the selection time limit.
        /// </summary>
        /// <returns>Returns true if the pointer can execute the select action.</returns>
        public virtual bool CanSelect()
        {
            return (Time.time >= selectDelayTimer);
        }

        /// <summary>
        /// The IsPointerActive method is used to determine if the pointer's current state is active or not.
        /// </summary>
        /// <returns>Returns true if the pointer is currently active.</returns>
        public virtual bool IsPointerActive()
        {
            return (currentActivationState != 0);
        }

        /// <summary>
        /// The ResetActivationTimer method is used to reset the pointer activation timer to the next valid activation time.
        /// </summary>
        /// <param name="forceZero">If this is true then the next activation time will be 0.</param>
        public virtual void ResetActivationTimer(bool forceZero = false)
        {
            activateDelayTimer = (forceZero ? 0f : Time.time + activationDelay);
        }

        /// <summary>
        /// The ResetSelectionTimer method is used to reset the pointer selection timer to the next valid activation time.
        /// </summary>
        /// <param name="forceZero">If this is true then the next activation time will be 0.</param>
        public virtual void ResetSelectionTimer(bool forceZero = false)
        {
            selectDelayTimer = (forceZero ? 0f : Time.time + selectionDelay);
        }

        /// <summary>
        /// The Toggle method is used to enable or disable the pointer.
        /// </summary>
        /// <param name="state">If true the pointer will be enabled if possible, if false the pointer will be disabled if possible.</param>
        public virtual void Toggle(bool state)
        {
            if (!CanActivate() || NoPointerRenderer() || CanActivateOnToggleButton(state))
            {
                return;
            }

            ManageActivationState(willDeactivate ? true : state);
            pointerRenderer.Toggle(IsPointerActive(), state);
            willDeactivate = false;
            if (!state)
            {
                StopUseAction();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            VRTK_PlayerObject.SetPlayerObject(gameObject, VRTK_PlayerObject.ObjectTypes.Pointer);
            customOrigin = (customOrigin == null ? VRTK_SDK_Bridge.GenerateControllerPointerOrigin(gameObject) : customOrigin);
            SetupController();
            SetupRenderer();
            activateDelayTimer = 0f;
            selectDelayTimer = 0f;
            hoverDurationTimer = 0f;
            currentActivationState = 0;
            wasActivated = false;
            willDeactivate = false;
            canClickOnHover = false;
            if (NoPointerRenderer())
            {
                Debug.LogWarning("The VRTK_Pointer script requires a VRTK_BasePointerRenderer specified as the `Pointer Renderer` parameter.");
            }
            if (activateOnEnable)
            {
                Toggle(true);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            UnsubscribeActivationButton();
            UnsubscribeSelectionButton();
        }

        protected virtual void Start()
        {
            FindController();
        }

        protected virtual void Update()
        {
            CheckButtonSubscriptions();
            if (EnabledPointerRenderer())
            {
                pointerRenderer.InitalizePointer(this, invalidListPolicy, navMeshCheckDistance, headsetPositionCompensation);
                pointerRenderer.UpdateRenderer();
                if (!IsPointerActive())
                {
                    bool currentPointerVisibility = pointerRenderer.IsVisible();
                    pointerRenderer.ToggleInteraction(currentPointerVisibility);
                }

                CheckHoverSelect();
            }

            UpdateDirectionIndicator();
        }

        protected virtual void UpdateDirectionIndicator()
        {
            if (directionIndicator != null && pointerRenderer != null)
            {
                RaycastHit destinationHit = pointerRenderer.GetDestinationHit();
                directionIndicator.SetPosition((IsPointerActive() && destinationHit.collider != null), destinationHit.point);
            }
        }

        protected virtual Quaternion? GetCursorRotation()
        {
            if (directionIndicator != null)
            {
                return directionIndicator.GetRotation();
            }
            return null;
        }

        protected virtual bool EnabledPointerRenderer()
        {
            return (pointerRenderer && pointerRenderer.enabled);
        }

        protected virtual bool NoPointerRenderer()
        {
            return (!pointerRenderer || !pointerRenderer.enabled);
        }

        protected virtual bool CanActivateOnToggleButton(bool state)
        {
            bool result = (state && !holdButtonToActivate && IsPointerActive());
            if (result)
            {
                willDeactivate = true;
            }
            return result;
        }

        protected virtual void FindController()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<VRTK_ControllerEvents>();
                SetupController();
            }

            if (controller == null && (activationButton != VRTK_ControllerEvents.ButtonAlias.Undefined || selectionButton != VRTK_ControllerEvents.ButtonAlias.Undefined))
            {
                Debug.LogWarning("`VRTK_Pointer` requires a Controller that has the `VRTK_ControllerEvents` script attached to it. To omit this warning, set the `Activation Bubtton` and `Selection Button` to `Undefined`");
            }

            if (directionIndicator != null)
            {
                directionIndicator.Initialize(controller);
            }
        }

        protected virtual void SetupController()
        {
            if (controller)
            {
                CheckButtonMappingConflict();
                SubscribeSelectionButton();
                SubscribeActivationButton();
            }
        }

        protected virtual void SetupRenderer()
        {
            if (EnabledPointerRenderer())
            {
                pointerRenderer.InitalizePointer(this, invalidListPolicy, navMeshCheckDistance, headsetPositionCompensation);
            }
        }

        protected virtual bool ButtonMappingIsUndefined(VRTK_ControllerEvents.ButtonAlias givenButton, VRTK_ControllerEvents.ButtonAlias givenSubscribedButton)
        {
            return (givenSubscribedButton != VRTK_ControllerEvents.ButtonAlias.Undefined && givenButton == VRTK_ControllerEvents.ButtonAlias.Undefined);
        }

        protected virtual void CheckButtonMappingConflict()
        {
            if (activationButton == selectionButton)
            {
                if (selectOnPress && holdButtonToActivate)
                {
                    Debug.LogWarning("Hold Button To Activate and Select On Press cannot both be checked when using the same button for Activation and Selection. Fixing by setting Select On Press to false.");
                }

                if (!selectOnPress && !holdButtonToActivate)
                {
                    Debug.LogWarning("Hold Button To Activate and Select On Press cannot both be unchecked when using the same button for Activation and Selection. Fixing by setting Select On Press to true.");
                }
                selectOnPress = !holdButtonToActivate;
            }
        }

        protected virtual void CheckButtonSubscriptions()
        {
            CheckButtonMappingConflict();

            if (ButtonMappingIsUndefined(selectionButton, subscribedSelectionButton) || selectOnPress != currentSelectOnPress)
            {
                UnsubscribeSelectionButton();
            }

            if (selectionButton != subscribedSelectionButton)
            {
                SubscribeSelectionButton();
                UnsubscribeActivationButton();
            }

            if (ButtonMappingIsUndefined(activationButton, subscribedActivationButton))
            {
                UnsubscribeActivationButton();
            }

            if (activationButton != subscribedActivationButton)
            {
                SubscribeActivationButton();
            }
        }

        protected virtual void SubscribeActivationButton()
        {
            if (subscribedActivationButton != VRTK_ControllerEvents.ButtonAlias.Undefined)
            {
                UnsubscribeActivationButton();
            }

            if (controller)
            {
                controller.SubscribeToButtonAliasEvent(activationButton, true, ActivationButtonPressed);
                controller.SubscribeToButtonAliasEvent(activationButton, false, ActivationButtonReleased);
                subscribedActivationButton = activationButton;
            }
        }

        protected virtual void UnsubscribeActivationButton()
        {
            if (controller && subscribedActivationButton != VRTK_ControllerEvents.ButtonAlias.Undefined)
            {
                controller.UnsubscribeToButtonAliasEvent(subscribedActivationButton, true, ActivationButtonPressed);
                controller.UnsubscribeToButtonAliasEvent(subscribedActivationButton, false, ActivationButtonReleased);
                subscribedActivationButton = VRTK_ControllerEvents.ButtonAlias.Undefined;
            }
        }

        protected virtual void ActivationButtonPressed(object sender, ControllerInteractionEventArgs e)
        {
            if (EnabledPointerRenderer())
            {
                controllerIndex = e.controllerIndex;
                Toggle(true);
            }
        }

        protected virtual void ActivationButtonReleased(object sender, ControllerInteractionEventArgs e)
        {
            if (EnabledPointerRenderer())
            {
                controllerIndex = e.controllerIndex;
                if (IsPointerActive())
                {
                    Toggle(false);
                }
            }
        }

        protected virtual void SubscribeSelectionButton()
        {
            if (subscribedSelectionButton != VRTK_ControllerEvents.ButtonAlias.Undefined)
            {
                UnsubscribeSelectionButton();
            }

            if (controller)
            {
                controller.SubscribeToButtonAliasEvent(selectionButton, selectOnPress, SelectionButtonAction);
                subscribedSelectionButton = selectionButton;
                currentSelectOnPress = selectOnPress;
            }
        }

        protected virtual void UnsubscribeSelectionButton()
        {
            if (controller && subscribedSelectionButton != VRTK_ControllerEvents.ButtonAlias.Undefined)
            {
                controller.UnsubscribeToButtonAliasEvent(subscribedSelectionButton, currentSelectOnPress, SelectionButtonAction);
                subscribedSelectionButton = VRTK_ControllerEvents.ButtonAlias.Undefined;
            }
        }

        protected virtual void SelectionButtonAction(object sender, ControllerInteractionEventArgs e)
        {
            controllerIndex = e.controllerIndex;
            ExecuteSelectionButtonAction();
        }

        protected virtual void ExecuteSelectionButtonAction()
        {
            if (EnabledPointerRenderer() && CanSelect() && (IsPointerActive() || wasActivated))
            {
                wasActivated = false;
                RaycastHit destinationHit = pointerRenderer.GetDestinationHit();
                AttemptUseOnSet(destinationHit.transform);
                if (destinationHit.transform && IsPointerActive() && pointerRenderer.ValidPlayArea() && !PointerActivatesUseAction(pointerInteractableObject) && pointerRenderer.IsValidCollision())
                {
                    ResetHoverSelectionTimer(destinationHit.collider);
                    ResetSelectionTimer();
                    OnDestinationMarkerSet(SetDestinationMarkerEvent(destinationHit.distance, destinationHit.transform, destinationHit, destinationHit.point, controllerIndex, false, GetCursorRotation()));
                }
            }
        }

        protected virtual bool CanResetActivationState(bool givenState)
        {
            return ((!givenState && holdButtonToActivate) || (givenState && !holdButtonToActivate && currentActivationState >= 2));
        }

        protected virtual void ManageActivationState(bool state)
        {
            if (state)
            {
                currentActivationState++;
            }

            wasActivated = (currentActivationState == 2);

            if (CanResetActivationState(state))
            {
                currentActivationState = 0;
            }
        }

        protected virtual bool PointerActivatesUseAction(VRTK_InteractableObject givenInteractableObject)
        {
            return (givenInteractableObject && givenInteractableObject.pointerActivatesUseAction && givenInteractableObject.IsValidInteractableController(controller.gameObject, givenInteractableObject.allowedUseControllers));
        }

        protected virtual void StartUseAction(Transform target)
        {
            pointerInteractableObject = target.GetComponent<VRTK_InteractableObject>();
            bool cannotUseBecauseNotGrabbed = (pointerInteractableObject && pointerInteractableObject.useOnlyIfGrabbed && !pointerInteractableObject.IsGrabbed());

            if (PointerActivatesUseAction(pointerInteractableObject) && pointerInteractableObject.holdButtonToUse && !cannotUseBecauseNotGrabbed && pointerInteractableObject.usingState == 0)
            {
                pointerInteractableObject.StartUsing(controller.gameObject);
                pointerInteractableObject.usingState++;
            }
        }

        protected virtual void StopUseAction()
        {
            if (PointerActivatesUseAction(pointerInteractableObject) && pointerInteractableObject.holdButtonToUse && pointerInteractableObject.IsUsing())
            {
                pointerInteractableObject.StopUsing(controller.gameObject);
                pointerInteractableObject.usingState = 0;
            }
        }

        protected virtual void AttemptUseOnSet(Transform target)
        {
            if (pointerInteractableObject && target)
            {
                if (PointerActivatesUseAction(pointerInteractableObject))
                {
                    if (pointerInteractableObject.IsUsing())
                    {
                        pointerInteractableObject.StopUsing(controller.gameObject);
                        pointerInteractableObject.usingState = 0;
                    }
                    else if (!pointerInteractableObject.holdButtonToUse)
                    {
                        pointerInteractableObject.StartUsing(controller.gameObject);
                        pointerInteractableObject.usingState++;
                    }
                }
            }
        }

        protected virtual void SetHoverSelectionTimer(Collider collider)
        {
            if (collider != currentCollider)
            {
                hoverDurationTimer = 0f;
            }

            if (selectAfterHoverDuration > 0f && hoverDurationTimer <= 0f)
            {
                canClickOnHover = true;
                hoverDurationTimer = selectAfterHoverDuration;
            }

            currentCollider = collider;
        }

        protected virtual void ResetHoverSelectionTimer(Collider collider)
        {
            canClickOnHover = false;
            hoverDurationTimer = (collider == currentCollider ? 0f : hoverDurationTimer);
        }

        protected virtual void CheckHoverSelect()
        {
            if (hoverDurationTimer > 0f)
            {
                hoverDurationTimer -= Time.deltaTime;
            }

            if (canClickOnHover && hoverDurationTimer <= 0f)
            {
                canClickOnHover = false;
                ExecuteSelectionButtonAction();
            }
        }
    }
}