using System.Numerics;
using System;

namespace FlashEditor.Rendering
{
    public static class ParticleBillboards
    {
        public const int VerticesPerParticle = 4;

        public const int IndicesPerParticle = 6;

        public const int FloatsPerParticle = 48;

        public static uint[] BuildIndices(int capacity)
        {
            uint[] array = new uint[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                uint num = (uint)(i * 4);
                int num2 = i * 6;
                array[num2] = num;
                array[num2 + 1] = num + 1;
                array[num2 + 2] = num + 2;
                array[num2 + 3] = num;
                array[num2 + 4] = num + 2;
                array[num2 + 5] = num + 3;
            }
            return array;
        }

        public static int Build(ParticleSystem system, Vector3 cameraRight, Vector3 cameraUp, Vector3 lightDirection, float[] buffer)
        {
            if (system == null)
            {
                throw new ArgumentNullException("system");
            }
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            int liveParticleCount = system.LiveParticleCount;
            if (buffer.Length < liveParticleCount * 48)
            {
                throw new ArgumentException("The buffer holds " + buffer.Length / 48 + " particles and " + liveParticleCount + " are alive.", "buffer");
            }
            Vector3 normal = ((lightDirection.LengthSquared() > 1E-12f) ? Vector3.Normalize(lightDirection) : Vector3.UnitY);
            for (int i = 0; i < liveParticleCount; i++)
            {
                Particle particle = system.ParticleAt(i);
                Vector3 vector = RenderSpace.ToWorld(particle.X >> 12, particle.Y >> 12, particle.Z >> 12);
                float num = (float)(particle.Size >> 12) / 128f;
                Vector3 vector2 = cameraRight * num;
                Vector3 vector3 = cameraUp * num;
                Vector3 colour = OverlayGeometry.Unlit(new Vector3((float)particle.Red / 255f, (float)particle.Green / 255f, (float)particle.Blue / 255f));
                float opacity = (float)particle.Alpha / 255f;
                int num2 = i * 4;
                WriteCorner(buffer, num2, vector - vector2 - vector3, normal, colour, opacity, 0f, 0f);
                WriteCorner(buffer, num2 + 1, vector + vector2 - vector3, normal, colour, opacity, 1f, 0f);
                WriteCorner(buffer, num2 + 2, vector + vector2 + vector3, normal, colour, opacity, 1f, 1f);
                WriteCorner(buffer, num2 + 3, vector - vector2 + vector3, normal, colour, opacity, 0f, 1f);
            }
            return liveParticleCount;
        }

        private static void WriteCorner(float[] buffer, int vertex, Vector3 position, Vector3 normal, Vector3 colour, float opacity, float u, float v)
        {
            OverlayGeometry.Write(buffer, vertex, position, normal, colour, opacity);
            int num = vertex * 12;
            buffer[num + 6] = u;
            buffer[num + 7] = v;
        }
    }
}
