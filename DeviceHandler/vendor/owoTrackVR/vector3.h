#pragma once

#include "shared.h"

class Basis;

struct Vector3
{
	enum Axis
	{
		AXIS_X,
		AXIS_Y,
		AXIS_Z,
	};

	union
	{
		struct
		{
			double x;
			double y;
			double z;
		};

		double coord[3] = {0};
	};

	const double& operator[](int p_axis) const
	{
		return coord[p_axis];
	}

	double& operator[](int p_axis)
	{
		return coord[p_axis];
	}

	void set_axis(int p_axis, double p_value);
	[[nodiscard]] double get_axis(int p_axis) const;

	[[nodiscard]] int min_axis() const;
	[[nodiscard]] int max_axis() const;

	[[nodiscard]] inline double length() const;
	[[nodiscard]] inline double length_squared() const;

	inline void normalize();
	[[nodiscard]] inline Vector3 normalized() const;
	[[nodiscard]] inline bool is_normalized() const;
	[[nodiscard]] inline Vector3 inverse() const;

	inline void zero();

	// void snap(Vector3 p_val);
	// Vector3 snapped(Vector3 p_val) const;

	void rotate(const Vector3& p_axis, double p_phi);
	[[nodiscard]] Vector3 rotated(const Vector3& p_axis, double p_phi) const;

	/* Static Methods between 2 vector3s */

	[[nodiscard]] inline Vector3 lerp(const Vector3& p_b, double p_t) const;
	[[nodiscard]] inline Vector3 slerp(const Vector3& p_b, double p_t) const;
	[[nodiscard]] Vector3 cubic_interpolate(const Vector3& p_b, const Vector3& p_pre_a, const Vector3& p_post_b,
	                                        double p_t) const;
	[[nodiscard]] Vector3 cubic_interpolaten(const Vector3& p_b, const Vector3& p_pre_a, const Vector3& p_post_b,
	                                         double p_t) const;
	[[nodiscard]] Vector3 move_toward(const Vector3& p_to, double p_delta) const;

	[[nodiscard]] inline Vector3 cross(const Vector3& p_b) const;
	[[nodiscard]] inline double dot(const Vector3& p_b) const;
	[[nodiscard]] Basis outer(const Vector3& p_b) const;
	[[nodiscard]] Basis to_diagonal_matrix() const;

	[[nodiscard]] inline Vector3 abs() const;
	[[nodiscard]] inline Vector3 floor() const;
	[[nodiscard]] inline Vector3 sign() const;
	[[nodiscard]] inline Vector3 ceil() const;
	[[nodiscard]] inline Vector3 round() const;

	[[nodiscard]] inline double distance_to(const Vector3& p_b) const;
	[[nodiscard]] inline double distance_squared_to(const Vector3& p_b) const;

	[[nodiscard]] inline Vector3 posmod(double p_mod) const;
	[[nodiscard]] inline Vector3 posmodv(const Vector3& p_modv) const;
	[[nodiscard]] inline Vector3 project(const Vector3& p_b) const;

	[[nodiscard]] inline double angle_to(const Vector3& p_b) const;
	[[nodiscard]] inline Vector3 direction_to(const Vector3& p_b) const;

	[[nodiscard]] inline Vector3 slide(const Vector3& p_normal) const;
	[[nodiscard]] inline Vector3 bounce(const Vector3& p_normal) const;
	[[nodiscard]] inline Vector3 reflect(const Vector3& p_normal) const;

	[[nodiscard]] bool is_equal_approx(const Vector3& p_v) const;

	/* Operators */

	inline Vector3& operator+=(const Vector3& p_v);
	inline Vector3 operator+(const Vector3& p_v) const;
	inline Vector3& operator-=(const Vector3& p_v);
	inline Vector3 operator-(const Vector3& p_v) const;
	inline Vector3& operator*=(const Vector3& p_v);
	inline Vector3 operator*(const Vector3& p_v) const;
	inline Vector3& operator/=(const Vector3& p_v);
	inline Vector3 operator/(const Vector3& p_v) const;

	inline Vector3& operator*=(double p_scalar);
	inline Vector3 operator*(double p_scalar) const;
	inline Vector3& operator/=(double p_scalar);
	inline Vector3 operator/(double p_scalar) const;

	inline Vector3 operator-() const;

	inline bool operator==(const Vector3& p_v) const;
	inline bool operator!=(const Vector3& p_v) const;
	inline bool operator<(const Vector3& p_v) const;
	inline bool operator<=(const Vector3& p_v) const;
	inline bool operator>(const Vector3& p_v) const;
	inline bool operator>=(const Vector3& p_v) const;

	Vector3()
	= default;

	Vector3(double p_x, double p_y, double p_z)
	{
		x = p_x;
		y = p_y;
		z = p_z;
	}
};

Vector3 Vector3::cross(const Vector3& p_b) const
{
	const Vector3 ret(
		(y * p_b.z) - (z * p_b.y),
		(z * p_b.x) - (x * p_b.z),
		(x * p_b.y) - (y * p_b.x));

	return ret;
}

double Vector3::dot(const Vector3& p_b) const
{
	return x * p_b.x + y * p_b.y + z * p_b.z;
}

Vector3 Vector3::abs() const
{
	return {std::abs(x), std::abs(y), std::abs(z)};
}

Vector3 Vector3::sign() const
{
	return {Math::sign_d(x), Math::sign_d(y), Math::sign_d(z)};
}

Vector3 Vector3::floor() const
{
	return Vector3(std::floor(x), std::floor(y), std::floor(z));
}

Vector3 Vector3::ceil() const
{
	return Vector3(std::ceil(x), std::ceil(y), std::ceil(z));
}

Vector3 Vector3::round() const
{
	return Vector3(std::round(x), std::round(y), std::round(z));
}

Vector3 Vector3::lerp(const Vector3& p_b, double p_t) const
{
	return {
		x + (p_t * (p_b.x - x)),
		y + (p_t * (p_b.y - y)),
		z + (p_t * (p_b.z - z))
	};
}

Vector3 Vector3::slerp(const Vector3& p_b, double p_t) const
{
	const double theta = angle_to(p_b);
	return rotated(cross(p_b).normalized(), theta * p_t);
}

double Vector3::distance_to(const Vector3& p_b) const
{
	return (p_b - *this).length();
}

double Vector3::distance_squared_to(const Vector3& p_b) const
{
	return (p_b - *this).length_squared();
}

Vector3 Vector3::posmod(const double p_mod) const
{
	return {Math::fposmod(x, p_mod), Math::fposmod(y, p_mod), Math::fposmod(z, p_mod)};
}

Vector3 Vector3::posmodv(const Vector3& p_modv) const
{
	return {Math::fposmod(x, p_modv.x), Math::fposmod(y, p_modv.y), Math::fposmod(z, p_modv.z)};
}

Vector3 Vector3::project(const Vector3& p_b) const
{
	return p_b * (dot(p_b) / p_b.length_squared());
}

double Vector3::angle_to(const Vector3& p_b) const
{
	return std::atan2(cross(p_b).length(), dot(p_b));
}

Vector3 Vector3::direction_to(const Vector3& p_b) const
{
	Vector3 ret(p_b.x - x, p_b.y - y, p_b.z - z);
	ret.normalize();
	return ret;
}

/* Operators */

Vector3& Vector3::operator+=(const Vector3& p_v)
{
	x += p_v.x;
	y += p_v.y;
	z += p_v.z;
	return *this;
}

Vector3 Vector3::operator+(const Vector3& p_v) const
{
	return {x + p_v.x, y + p_v.y, z + p_v.z};
}

Vector3& Vector3::operator-=(const Vector3& p_v)
{
	x -= p_v.x;
	y -= p_v.y;
	z -= p_v.z;
	return *this;
}

Vector3 Vector3::operator-(const Vector3& p_v) const
{
	return {x - p_v.x, y - p_v.y, z - p_v.z};
}

Vector3& Vector3::operator*=(const Vector3& p_v)
{
	x *= p_v.x;
	y *= p_v.y;
	z *= p_v.z;
	return *this;
}

Vector3 Vector3::operator*(const Vector3& p_v) const
{
	return {x * p_v.x, y * p_v.y, z * p_v.z};
}

Vector3& Vector3::operator/=(const Vector3& p_v)
{
	x /= p_v.x;
	y /= p_v.y;
	z /= p_v.z;
	return *this;
}

Vector3 Vector3::operator/(const Vector3& p_v) const
{
	return {x / p_v.x, y / p_v.y, z / p_v.z};
}

Vector3& Vector3::operator*=(double p_scalar)
{
	x *= p_scalar;
	y *= p_scalar;
	z *= p_scalar;
	return *this;
}

inline Vector3 operator*(double p_scalar, const Vector3& p_vec)
{
	return p_vec * p_scalar;
}

Vector3 Vector3::operator*(double p_scalar) const
{
	return {x * p_scalar, y * p_scalar, z * p_scalar};
}

Vector3& Vector3::operator/=(double p_scalar)
{
	x /= p_scalar;
	y /= p_scalar;
	z /= p_scalar;
	return *this;
}

Vector3 Vector3::operator/(double p_scalar) const
{
	return {x / p_scalar, y / p_scalar, z / p_scalar};
}

Vector3 Vector3::operator-() const
{
	return {-x, -y, -z};
}

bool Vector3::operator==(const Vector3& p_v) const
{
	return x == p_v.x && y == p_v.y && z == p_v.z;
}

bool Vector3::operator!=(const Vector3& p_v) const
{
	return x != p_v.x || y != p_v.y || z != p_v.z;
}

bool Vector3::operator<(const Vector3& p_v) const
{
	if (x == p_v.x)
	{
		if (y == p_v.y)
		{
			return z < p_v.z;
		}
		return y < p_v.y;
	}
	return x < p_v.x;
}

bool Vector3::operator>(const Vector3& p_v) const
{
	if (x == p_v.x)
	{
		if (y == p_v.y)
		{
			return z > p_v.z;
		}
		return y > p_v.y;
	}
	return x > p_v.x;
}

bool Vector3::operator<=(const Vector3& p_v) const
{
	if (x == p_v.x)
	{
		if (y == p_v.y)
		{
			return z <= p_v.z;
		}
		return y < p_v.y;
	}
	return x < p_v.x;
}

bool Vector3::operator>=(const Vector3& p_v) const
{
	if (x == p_v.x)
	{
		if (y == p_v.y)
		{
			return z >= p_v.z;
		}
		return y > p_v.y;
	}
	return x > p_v.x;
}

inline Vector3 vec3_cross(const Vector3& p_a, const Vector3& p_b)
{
	return p_a.cross(p_b);
}

inline double vec3_dot(const Vector3& p_a, const Vector3& p_b)
{
	return p_a.dot(p_b);
}

double Vector3::length() const
{
	const double x2 = x * x;
	const double y2 = y * y;
	const double z2 = z * z;

	return std::sqrt(x2 + y2 + z2);
}

double Vector3::length_squared() const
{
	const double x2 = x * x;
	const double y2 = y * y;
	const double z2 = z * z;

	return x2 + y2 + z2;
}

void Vector3::normalize()
{
	const double lengthsq = length_squared();
	if (lengthsq == 0)
	{
		x = y = z = 0;
	}
	else
	{
		const double length = std::sqrt(lengthsq);
		x /= length;
		y /= length;
		z /= length;
	}
}

Vector3 Vector3::normalized() const
{
	Vector3 v = *this;
	v.normalize();
	return v;
}

bool Vector3::is_normalized() const
{
	// use length_squared() instead of length() to avoid sqrt(), makes it more stringent.
	return Math::is_equal_approx(length_squared(), 1.0, UNIT_EPSILON);
}

Vector3 Vector3::inverse() const
{
	return {1.0 / x, 1.0 / y, 1.0 / z};
}

void Vector3::zero()
{
	x = y = z = 0;
}

// slide returns the component of the vector along the given plane, specified by its normal vector.
Vector3 Vector3::slide(const Vector3& p_normal) const
{
#ifdef MATH_CHECKS
	ERR_FAIL_COND_V_MSG(!p_normal.is_normalized(), Vector3(), "The normal Vector3 must be normalized.");
#endif
	return *this - p_normal * this->dot(p_normal);
}

Vector3 Vector3::bounce(const Vector3& p_normal) const
{
	return -reflect(p_normal);
}

Vector3 Vector3::reflect(const Vector3& p_normal) const
{
#ifdef MATH_CHECKS
	ERR_FAIL_COND_V_MSG(!p_normal.is_normalized(), Vector3(), "The normal Vector3 must be normalized.");
#endif
	return 2.0 * p_normal * this->dot(p_normal) - *this;
}
