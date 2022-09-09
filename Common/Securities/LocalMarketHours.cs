/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static QuantConnect.StringExtensions;

namespace QuantConnect.Securities
{
    /// <summary>
    /// Represents the market hours under normal conditions for an exchange and a specific day of the week in terms of local time
    /// </summary>
    public class LocalMarketHours
    {
        /// <summary>
        /// Gets whether or not this exchange is closed all day
        /// </summary>
        public bool IsClosedAllDay { get; }

        /// <summary>
        /// Gets whether or not this exchange is closed all day
        /// </summary>
        public bool IsOpenAllDay { get; }

        /// <summary>
        /// Gets the day of week these hours apply to
        /// </summary>
        public DayOfWeek DayOfWeek { get; }

        /// <summary>
        /// Gets the tradable time during the market day.
        /// For a normal US equity trading day this is 6.5 hours.
        /// This does NOT account for extended market hours and only
        /// considers <see cref="MarketHoursState.Market"/>
        /// </summary>
        public TimeSpan MarketDuration { get; }

        /// <summary>
        /// Gets the individual market hours segments that define the hours of operation for this day
        /// </summary>
        public ReadOnlyCollection<MarketHoursSegment> Segments { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalMarketHours"/> class
        /// </summary>
        /// <param name="day">The day of the week these hours are applicable</param>
        /// <param name="segments">The open/close segments defining the market hours for one day</param>
        public LocalMarketHours(DayOfWeek day, params MarketHoursSegment[] segments)
            : this(day, (IEnumerable<MarketHoursSegment>) segments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalMarketHours"/> class
        /// </summary>
        /// <param name="day">The day of the week these hours are applicable</param>
        /// <param name="segments">The open/close segments defining the market hours for one day</param>
        public LocalMarketHours(DayOfWeek day, IEnumerable<MarketHoursSegment> segments)
        {
            DayOfWeek = day;
            // filter out the closed states, we'll assume closed if no segment exists
            Segments = new ReadOnlyCollection<MarketHoursSegment>((segments ?? Enumerable.Empty<MarketHoursSegment>()).Where(x => x.State != MarketHoursState.Closed).ToList());
            IsClosedAllDay = Segments.Count == 0;
            IsOpenAllDay = Segments.Count == 1
                && Segments[0].Start == TimeSpan.Zero
                && Segments[0].End == Time.OneDay
                && Segments[0].State == MarketHoursState.Market;

            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment.State == MarketHoursState.Market)
                {
                    MarketDuration += segment.End - segment.Start;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalMarketHours"/> class from the specified open/close times
        /// </summary>
        /// <param name="day">The day of week these hours apply to</param>
        /// <param name="extendedMarketOpen">The extended market open time</param>
        /// <param name="marketOpen">The regular market open time, must be greater than or equal to the extended market open time</param>
        /// <param name="marketClose">The regular market close time, must be greater than the regular market open time</param>
        /// <param name="extendedMarketClose">The extended market close time, must be greater than or equal to the regular market close time</param>
        public LocalMarketHours(DayOfWeek day, TimeSpan extendedMarketOpen, TimeSpan marketOpen, TimeSpan marketClose, TimeSpan extendedMarketClose)
            : this(day, MarketHoursSegment.GetMarketHoursSegments(extendedMarketOpen, marketOpen, marketClose, extendedMarketClose))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalMarketHours"/> class from the specified open/close times
        /// using the market open as the extended market open and the market close as the extended market close, effectively
        /// removing any 'extended' session from these exchange hours
        /// </summary>
        /// <param name="day">The day of week these hours apply to</param>
        /// <param name="marketOpen">The regular market open time</param>
        /// <param name="marketClose">The regular market close time, must be greater than the regular market open time</param>
        public LocalMarketHours(DayOfWeek day, TimeSpan marketOpen, TimeSpan marketClose)
            : this(day, marketOpen, marketOpen, marketClose, marketClose)
        {
        }

        /// <summary>
        /// Gets the market opening time of day
        /// </summary>
        /// <param name="time">The reference time, the open returned will be the first open after the specified time if there are multiple market open segments</param>
        /// <param name="extendedMarket">True to include extended market hours, false for regular market hours</param>
        /// <param name="previousDayLastSegment">The previous days last segment. This is used when the potential next market open is the first segment of the day
        /// so we need to check that segment is not part of previous day last segment. If null, it means there were no segments on the last day</param>
        /// <returns>The market's opening time of day</returns>
        public TimeSpan? GetMarketOpen(TimeSpan time, bool extendedMarket, TimeSpan? previousDayLastSegment = null)
        {
            var previousSegment = previousDayLastSegment;
            bool prevSegmentIsFromPrevDay = true;
            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment.State == MarketHoursState.Closed || segment.End <= time)
                {
                    // update prev segment end time only if the current segment could have been taken into account
                    // (regular hours or, when enabled, extended hours segment)
                    if (segment.State == MarketHoursState.Market || extendedMarket)
                    {
                        previousSegment = segment.End;
                        prevSegmentIsFromPrevDay = false;
                    }

                    continue;
                }

                // let's try this segment if it's regular market hours or if it is extended market hours and extended market is allowed
                if (segment.State == MarketHoursState.Market || extendedMarket)
                {
                    if (!IsContinuousMarketOpen(previousSegment, segment.Start, prevSegmentIsFromPrevDay))
                    {
                        return segment.Start;
                    }

                    previousSegment = segment.End;
                    prevSegmentIsFromPrevDay = false;
                }
            }

            // we couldn't locate an open segment after the specified time
            return null;
        }

        /// <summary>
        /// Gets the market closing time of day
        /// </summary>
        /// <param name="time">The reference time, the close returned will be the first close after the specified time if there are multiple market open segments</param>
        /// <param name="extendedMarket">True to include extended market hours, false for regular market hours</param>
        /// <param name="nextDaySegmentStart">Next day first segment start. This is used when the potential next market close is
        /// the last segment of the day so we need to check that segment is not continued on next day first segment.
        /// If null, it means there are no segments on the next day</param>
        /// <returns>The market's closing time of day</returns>
        public TimeSpan? GetMarketClose(TimeSpan time, bool extendedMarket, TimeSpan? nextDaySegmentStart = null)
        {
            TimeSpan? nextSegment;
            bool nextSegmentIsFromNextDay = false;
            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment.State == MarketHoursState.Closed || segment.End <= time)
                {
                    continue;
                }

                if (i != Segments.Count - 1)
                {
                    var potentialNextSegment = Segments[i+1];

                    // Check whether we can consider PostMarket or not
                    if (potentialNextSegment.State != MarketHoursState.Market && !extendedMarket)
                    {
                        nextSegment = null;
                    }
                    else
                    {
                        nextSegment = Segments[i+1].Start;
                    }
                }
                else
                {
                    nextSegment = nextDaySegmentStart;
                    nextSegmentIsFromNextDay = true;
                }

                if ((segment.State == MarketHoursState.Market || extendedMarket) &&
                    !IsContinuousMarketOpen(segment.End, nextSegment, nextSegmentIsFromNextDay))
                {
                    return segment.End;
                }
            }

            // we couldn't locate an open segment after the specified time
            return null;
        }

        /// <summary>
        /// Determines if the exchange is open at the specified time
        /// </summary>
        /// <param name="time">The time of day to check</param>
        /// <param name="extendedMarket">True to check exended market hours, false to check regular market hours</param>
        /// <returns>True if the exchange is considered open, false otherwise</returns>
        public bool IsOpen(TimeSpan time, bool extendedMarket)
        {
            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment.State == MarketHoursState.Closed)
                {
                    continue;
                }

                if (segment.Contains(time))
                {
                    return extendedMarket || segment.State == MarketHoursState.Market;
                }
            }

            // if we didn't find a segment then we're closed
            return false;
        }

        /// <summary>
        /// Determines if the exchange is open during the specified interval
        /// </summary>
        /// <param name="start">The start time of the interval</param>
        /// <param name="end">The end time of the interval</param>
        /// <param name="extendedMarket">True to check exended market hours, false to check regular market hours</param>
        /// <returns>True if the exchange is considered open, false otherwise</returns>
        public bool IsOpen(TimeSpan start, TimeSpan end, bool extendedMarket)
        {
            if (start == end)
            {
                return IsOpen(start, extendedMarket);
            }

            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment.State == MarketHoursState.Closed)
                {
                    continue;
                }

                if (extendedMarket || segment.State == MarketHoursState.Market)
                {
                    if (segment.Overlaps(start, end))
                    {
                        return true;
                    }
                }
            }

            // if we didn't find a segment then we're closed
            return false;
        }

        /// <summary>
        /// Gets a <see cref="LocalMarketHours"/> instance that is always closed
        /// </summary>
        /// <param name="dayOfWeek">The day of week</param>
        /// <returns>A <see cref="LocalMarketHours"/> instance that is always closed</returns>
        public static LocalMarketHours ClosedAllDay(DayOfWeek dayOfWeek)
        {
            return new LocalMarketHours(dayOfWeek);
        }

        /// <summary>
        /// Gets a <see cref="LocalMarketHours"/> instance that is always open
        /// </summary>
        /// <param name="dayOfWeek">The day of week</param>
        /// <returns>A <see cref="LocalMarketHours"/> instance that is always open</returns>
        public static LocalMarketHours OpenAllDay(DayOfWeek dayOfWeek)
        {
            return new LocalMarketHours(dayOfWeek, new MarketHoursSegment(MarketHoursState.Market, TimeSpan.Zero, Time.OneDay));
        }

        /// <summary>
        /// Check the given segment is not part of the current previous segment
        /// </summary>
        /// <param name="previousSegmentEnd">Previous segment end time before the current segment</param>
        /// <param name="nextSegmentStart">The next segment start time</param>
        /// <param name="prevSegmentIsFromPrevDay">Indicated whether the previous segment is from the previous day or not
        /// (then it is from the same day as the next segment). Defaults to true</param>
        /// <returns>True if indeed the given segment is part of the last segment. False otherwise</returns>
        public static bool IsContinuousMarketOpen(TimeSpan? previousSegmentEnd, TimeSpan? nextSegmentStart, bool prevSegmentIsFromPrevDay = true)
        {
            if (previousSegmentEnd != null && nextSegmentStart != null)
            {
                if (prevSegmentIsFromPrevDay)
                {
                    // midnight passing to the next day
                    return previousSegmentEnd.Value == Time.OneDay && nextSegmentStart.Value == TimeSpan.Zero;
                }

                // passing from one segment to another in the same day
                return previousSegmentEnd.Value == nextSegmentStart.Value;
            }
            return false;
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            if (IsClosedAllDay)
            {
                return "Closed All Day";
            }
            if (IsOpenAllDay)
            {
                return "Open All Day";
            }

            return Invariant($"{DayOfWeek}: {string.Join(" | ", Segments)}");
        }
    }
}
