using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObstacleMovement : MonoBehaviour
{
    [Header("Floating Settings")]
    public float FloatSpeed = 1f;
    public float FloatAmplitude = 0.5f;

    [Header("Forward Movement Settings")]
    public float MoveSpeed = 2f;
    public float MoveDistance = 11.5f;
    public float RotationAngle = 90f;

    private Vector3 _startPosition;
    private Vector3 _moveStartPosition;
    private bool _hasRotated = false;
    private Quaternion _targetRotation;

    void Start()
    {
        _startPosition = transform.position;
        _moveStartPosition = transform.position;
        _targetRotation = transform.rotation * Quaternion.Euler(0, 0, RotationAngle);
    }

    void Update()
    {
        HandleFloating();
        HandleForwardMovement();
    }

    void HandleFloating()
    {
        float newY = _startPosition.y + Mathf.Sin(Time.time * FloatSpeed) * FloatAmplitude;
        Vector3 pos = transform.position;
        transform.position = new Vector3(pos.x, newY, pos.z);
    }

    void HandleForwardMovement()
    {
        if (!_hasRotated)
        {
            float movedDistance = Vector3.Distance(_moveStartPosition, transform.position);
            if (movedDistance < MoveDistance)
            {
                // Move forward in local forward direction
                transform.Translate(Vector3.forward * MoveSpeed * Time.deltaTime, Space.Self);
            }
            else
            {
                // Rotate 90 degrees on Z axis once
                transform.rotation = _targetRotation;
                _hasRotated = true;

                // Optionally, reset movement if looping is desired
                // _moveStartPosition = transform.position;
                // _hasRotated = false;
            }
        }
    }
}
