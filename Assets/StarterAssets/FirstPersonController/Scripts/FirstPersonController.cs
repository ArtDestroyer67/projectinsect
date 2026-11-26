using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		// Source units -> meters: 1 unit = 0.01905 m (16 units = 1 foot).
		// Defaults below are Half-Life 2 / CS-style values converted to meters.

		[Header("Player (Source-style movement)")]
		[Tooltip("Max ground wish speed in m/s (HL2 sv_maxspeed 320 = ~6.1)")]
		public float MoveSpeed = 6.1f;
		[Tooltip("Max ground wish speed while sprint is held in m/s")]
		public float SprintSpeed = 8.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;

		[Space(10)]
		[Tooltip("sv_accelerate: how quickly you reach wish speed on the ground")]
		public float GroundAcceleration = 10.0f;
		[Tooltip("sv_airaccelerate: how quickly you gain speed in the air (higher = easier strafe-jumping). HL2DM = 10, CS = 12, surf = 100+")]
		public float AirAcceleration = 10.0f;
		[Tooltip("The air wish-speed cap in m/s. Source uses 30 units = 0.57 m/s. This is what makes air-strafing work")]
		public float AirSpeedCap = 0.57f;
		[Tooltip("sv_friction: ground friction")]
		public float Friction = 4.0f;
		[Tooltip("sv_stopspeed: below this speed friction uses this value as the minimum, so you stop faster (100 units = 1.9 m/s)")]
		public float StopSpeed = 1.9f;

		[Space(10)]
		[Tooltip("The height the player can jump (Source: ~1.085 m)")]
		public float JumpHeight = 1.085f;
		[Tooltip("Hold jump to keep bunny hopping without re-pressing (like most Source servers with autohop)")]
		public bool AutoBunnyHop = true;
		[Tooltip("The character uses its own gravity value (Source: 800 u/s^2 = -15.24)")]
		public float Gravity = -15.24f;
		[Tooltip("Maximum fall speed in m/s")]
		public float TerminalVelocity = 53.0f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		[Header("Strafe Camera Tilt")]
		[Tooltip("Max roll in degrees when strafing (Source cl_rollangle). Set to 0 to disable. Negative values flip the direction")]
		public float TiltAngle = 3.0f;
		[Tooltip("Sideways speed in m/s at which the full tilt is reached (Source cl_rollspeed 200 units = 3.8)")]
		public float TiltFullSpeed = 3.8f;
		[Tooltip("How fast the camera rolls into and out of the tilt")]
		public float TiltSmoothing = 12.0f;

		// cinemachine
		private float _cinemachineTargetPitch;
		private float _currentRoll;

		// player
		private float _rotationVelocity;
		private Vector3 _velocity; // our own velocity, we do not rely on CharacterController.velocity

#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif
		}

		private void Update()
		{
			GroundedCheck();
			Move();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

			// like Source: if we are moving up fast we are not "on the ground", even if still touching it.
			// this stops friction from being applied on the frame we leave the ground
			if (_velocity.y > 0.5f)
			{
				Grounded = false;
			}
		}

		private void CameraRotation()
		{
			// pitch / yaw from look input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}

			// roll tilt (Source: roll = dot(velocity, right) * rollangle / rollspeed, clamped).
			// In Unity a positive Z rotation leans the camera left, so we negate to lean into the strafe.
			float sideSpeed = Vector3.Dot(_velocity, transform.right);
			float targetRoll = -Mathf.Clamp(sideSpeed / TiltFullSpeed, -1f, 1f) * TiltAngle;
			float t = 1f - Mathf.Exp(-TiltSmoothing * Time.deltaTime);
			_currentRoll = Mathf.Lerp(_currentRoll, targetRoll, t);

			// Update Cinemachine camera target pitch + roll (applied every frame so the tilt keeps easing out)
			CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, _currentRoll);
		}

		private void Move()
		{
			float dt = Time.deltaTime;

			// ---- wish direction / speed ----
			float maxSpeed = _input.sprint ? SprintSpeed : MoveSpeed;
			Vector3 wishDir = transform.right * _input.move.x + transform.forward * _input.move.y;
			wishDir.y = 0f;
			float wishSpeed = Mathf.Min(wishDir.magnitude, 1f) * maxSpeed;
			wishDir = wishDir.sqrMagnitude > 0.0001f ? wishDir.normalized : Vector3.zero;

			// ---- ground / air movement ----
			if (Grounded)
			{
				if (_input.jump)
				{
					// jump BEFORE friction is applied: this is what lets you keep your speed while bunny hopping
					_velocity.y = Mathf.Sqrt(JumpHeight * -2f * Gravity);
					Grounded = false;

					// without autohop, you have to press jump again for the next hop
					if (!AutoBunnyHop) _input.jump = false;

					AirAccelerate(wishDir, wishSpeed, dt);
				}
				else
				{
					ApplyFriction(dt);
					GroundAccelerate(wishDir, wishSpeed, dt);

					// keep the controller glued to the ground
					_velocity.y = -2f;
				}
			}
			else
			{
				AirAccelerate(wishDir, wishSpeed, dt);

				// if we are not grounded and autohop is off, buffered jump presses are dropped
				if (!AutoBunnyHop) _input.jump = false;
			}

			// ---- gravity (Source applies half before and half after the move for frame-rate independence) ----
			if (!Grounded) _velocity.y += Gravity * dt * 0.5f;

			CollisionFlags flags = _controller.Move(_velocity * dt);

			if (!Grounded) _velocity.y += Gravity * dt * 0.5f;
			_velocity.y = Mathf.Max(_velocity.y, -TerminalVelocity);

			// ---- collision response: slide along walls, bonk head on ceilings ----
			if ((flags & CollisionFlags.Sides) != 0)
			{
				// CharacterController.velocity is the actual velocity after sliding along the wall
				Vector3 actual = _controller.velocity;
				_velocity.x = actual.x;
				_velocity.z = actual.z;
			}
			if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f)
			{
				_velocity.y = 0f;
			}
		}

		// Source: CGameMovement::Friction
		private void ApplyFriction(float dt)
		{
			Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
			float speed = horizontal.magnitude;

			if (speed < 0.01f)
			{
				_velocity.x = 0f;
				_velocity.z = 0f;
				return;
			}

			float control = Mathf.Max(speed, StopSpeed);
			float drop = control * Friction * dt;
			float newSpeed = Mathf.Max(speed - drop, 0f) / speed;

			_velocity.x *= newSpeed;
			_velocity.z *= newSpeed;
		}

		// Source: CGameMovement::Accelerate
		private void GroundAccelerate(Vector3 wishDir, float wishSpeed, float dt)
		{
			if (wishSpeed <= 0f) return;

			float currentSpeed = Vector3.Dot(new Vector3(_velocity.x, 0f, _velocity.z), wishDir);
			float addSpeed = wishSpeed - currentSpeed;
			if (addSpeed <= 0f) return;

			float accelSpeed = Mathf.Min(GroundAcceleration * dt * wishSpeed, addSpeed);

			_velocity.x += accelSpeed * wishDir.x;
			_velocity.z += accelSpeed * wishDir.z;
		}

		// Source: CGameMovement::AirAccelerate
		// The wish speed is capped to a tiny value when comparing against current speed, but the acceleration
		// amount uses the full wish speed. That is the trick behind air-strafing: you can keep adding speed
		// perpendicular to your velocity, even when you are already going faster than the max speed.
		private void AirAccelerate(Vector3 wishDir, float wishSpeed, float dt)
		{
			if (wishSpeed <= 0f) return;

			float cappedWishSpeed = Mathf.Min(wishSpeed, AirSpeedCap);

			float currentSpeed = Vector3.Dot(new Vector3(_velocity.x, 0f, _velocity.z), wishDir);
			float addSpeed = cappedWishSpeed - currentSpeed;
			if (addSpeed <= 0f) return;

			float accelSpeed = Mathf.Min(AirAcceleration * wishSpeed * dt, addSpeed);

			_velocity.x += accelSpeed * wishDir.x;
			_velocity.z += accelSpeed * wishDir.z;
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}