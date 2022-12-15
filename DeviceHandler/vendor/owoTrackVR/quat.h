#pragma once

#include "winrt/DeviceHandler.h"

#include "shared.h"
#include "vector3.h"
#include <cmath>

class Basis;

class Quat
{
public:
	Quat(const winrt::DeviceHandler::Quaternion& q) :
		x(q.X), y(q.Y), z(q.Z), w(q.W)
	{
	}

	winrt::DeviceHandler::Quaternion toWinRT()
	{
		return {
			static_cast<float>(x),
			static_cast<float>(y),
			static_cast<float>(z),
			static_cast<float>(w)
		};
	}

	union
	{
		struct
		{
			double x;
			double y;
			double z;
			double w;
		};

		double components[4] = {0, 0, 0, 1.0};
	};

	double& operator[](int idx)
	{
		return components[idx];
	}

	const double& operator[](int idx) const
	{
		return components[idx];
	}

	[[nodiscard]] inline double length_squared() const;
	[[nodiscard]] bool is_equal_approx(const Quat& p_quat) const;
	[[nodiscard]] double length() const;
	void normalize();
	[[nodiscard]] Quat normalized() const;
	[[nodiscard]] bool is_normalized() const;
	[[nodiscard]] Quat inverse() const;
	[[nodiscard]] inline double dot(const Quat& q) const;

	void set_euler_xyz(const Vector3& p_euler);
	[[nodiscard]] Vector3 get_euler_xyz() const;
	void set_euler_yxz(const Vector3& p_euler);
	[[nodiscard]] Vector3 get_euler_yxz() const;

	void set_euler(const Vector3& p_euler) { set_euler_yxz(p_euler); };
	[[nodiscard]] Vector3 get_euler() const { return get_euler_yxz(); };

	[[nodiscard]] Quat slerp(const Quat& q, const double& t) const;
	[[nodiscard]] Quat slerpni(const Quat& q, const double& t) const;
	[[nodiscard]] Quat cubic_slerp(const Quat& q, const Quat& prep, const Quat& postq, const double& t) const;

	void set_axis_angle(const Vector3& axis, const double& angle);

	void get_axis_angle(Vector3& r_axis, double& r_angle) const
	{
		r_angle = 2 * std::acos(w);
		const double r = static_cast<double>(1) / std::sqrt(1 - w * w);
		r_axis.x = x * r;
		r_axis.y = y * r;
		r_axis.z = z * r;
	}

	void operator*=(const Quat& q);
	Quat operator*(const Quat& q) const;

	Quat operator*(const Vector3& v) const
	{
		return {
			w * v.x + y * v.z - z * v.y,
			w * v.y + z * v.x - x * v.z,
			w * v.z + x * v.y - y * v.x,
			-x * v.x - y * v.y - z * v.z
		};
	}

	[[nodiscard]] Vector3 xform(const Vector3& v) const
	{
#ifdef MATH_CHECKS
		ERR_FAIL_COND_V_MSG(!is_normalized(), v, "The quaternion must be normalized.");
#endif
		const Vector3 u(x, y, z);
		const Vector3 uv = u.cross(v);
		return v + ((uv * w) + u.cross(uv)) * 2;
	}

	[[nodiscard]] Vector3 xform_inv(const Vector3& v) const
	{
		return inverse().xform(v);
	}

	inline void operator+=(const Quat& q);
	inline void operator-=(const Quat& q);
	inline void operator*=(const double& s);
	inline void operator/=(const double& s);
	inline Quat operator+(const Quat& q2) const;
	inline Quat operator-(const Quat& q2) const;
	inline Quat operator-() const;
	inline Quat operator*(const double& s) const;
	inline Quat operator/(const double& s) const;

	inline bool operator==(const Quat& p_quat) const;
	inline bool operator!=(const Quat& p_quat) const;

	void set(double p_x, double p_y, double p_z, double p_w)
	{
		x = p_x;
		y = p_y;
		z = p_z;
		w = p_w;
	}

	Quat()
	= default;

	Quat(double p_x, double p_y, double p_z, double p_w) :
		x(p_x),
		y(p_y),
		z(p_z),
		w(p_w)
	{
	}

	Quat(const Vector3& axis, const double& angle) { set_axis_angle(axis, angle); }

	Quat(const Vector3& euler) { set_euler(euler); }

	Quat(const Quat& q) :
		x(q.x),
		y(q.y),
		z(q.z),
		w(q.w)
	{
	}

	Quat& operator=(const Quat& q)
	{
		x = q.x;
		y = q.y;
		z = q.z;
		w = q.w;
		return *this;
	}

	Quat(const Vector3& v0, const Vector3& v1) // shortest arc
	{
		const Vector3 c = v0.cross(v1);
		const double d = v0.dot(v1);

		if (d < -1.0 + CMP_EPSILON)
		{
			x = 0;
			y = 1;
			z = 0;
			w = 0;
		}
		else
		{
			const double s = std::sqrt((1.0 + d) * 2.0);
			const double rs = 1.0 / s;

			x = c.x * rs;
			y = c.y * rs;
			z = c.z * rs;
			w = s * 0.5;
		}
	}
};

double Quat::dot(const Quat& q) const
{
	return x * q.x + y * q.y + z * q.z + w * q.w;
}

double Quat::length_squared() const
{
	return dot(*this);
}

void Quat::operator+=(const Quat& q)
{
	x += q.x;
	y += q.y;
	z += q.z;
	w += q.w;
}

void Quat::operator-=(const Quat& q)
{
	x -= q.x;
	y -= q.y;
	z -= q.z;
	w -= q.w;
}

void Quat::operator*=(const double& s)
{
	x *= s;
	y *= s;
	z *= s;
	w *= s;
}

void Quat::operator/=(const double& s)
{
	*this *= 1.0 / s;
}

Quat Quat::operator+(const Quat& q2) const
{
	const Quat& q1 = *this;
	return {q1.x + q2.x, q1.y + q2.y, q1.z + q2.z, q1.w + q2.w};
}

Quat Quat::operator-(const Quat& q2) const
{
	const Quat& q1 = *this;
	return {q1.x - q2.x, q1.y - q2.y, q1.z - q2.z, q1.w - q2.w};
}

Quat Quat::operator-() const
{
	const Quat& q2 = *this;
	return {-q2.x, -q2.y, -q2.z, -q2.w};
}

Quat Quat::operator*(const double& s) const
{
	return {x * s, y * s, z * s, w * s};
}

Quat Quat::operator/(const double& s) const
{
	return *this * (1.0 / s);
}

bool Quat::operator==(const Quat& p_quat) const
{
	return x == p_quat.x && y == p_quat.y && z == p_quat.z && w == p_quat.w;
}

bool Quat::operator!=(const Quat& p_quat) const
{
	return x != p_quat.x || y != p_quat.y || z != p_quat.z || w != p_quat.w;
}

inline Quat operator*(const double& p_real, const Quat& p_quat)
{
	return p_quat * p_real;
}
